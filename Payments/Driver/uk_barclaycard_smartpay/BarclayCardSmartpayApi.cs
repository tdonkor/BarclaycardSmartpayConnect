using Acrelec.Library.Logger;
using Acrelec.Mockingbird.Payment.Configuration;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Timers;
using System.Xml;
using System.Xml.Linq;

namespace Acrelec.Mockingbird.Payment
{
    public class BarclayCardSmartpayApi : IDisposable
    {
        //Trans_NUM details incrementing each time
        static int number = 0;
        string transNum = "000000";
        const string transactionLimit = "9999998";

        //Reference variables
        string toBeSearched = "reference=";
        string reference = string.Empty;

        //socket variables
        IPHostEntry ipHostInfo;
        IPAddress ipAddress;
        IPEndPoint remoteEP;

        // payment success flag
        DiagnosticErrMsg isSuccessful = DiagnosticErrMsg.OK;

        // payment response flag
        private string paymentSuccessful = string.Empty;

        //bool Authorisation successful flag
        private bool authorisationFlag = false;

        // Data buffer for incoming data.
        //make large enough to take the largest return
        byte[] bytes = new byte[3096];

        //Ini file data 
        AppConfiguration configFile;
        int currency;
        int country;
        int port;
        string sourceId;


        /// <summary>
        /// Constructor
        /// </summary>
        public BarclayCardSmartpayApi()
        {
            // Establish the remote endpoint for the socket.  
            // This example uses port 8000 on the local computer.  
            // get ini file values
            configFile = AppConfiguration.Instance;
            currency = Convert.ToInt32(configFile.Currency);
            country = Convert.ToInt32(configFile.Country);
            port = Convert.ToInt32(configFile.Port);
            sourceId = configFile.SourceId;

            ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            ipAddress = ipHostInfo.AddressList[0];
            remoteEP = new IPEndPoint(ipAddress, port);
        }

        /// <summary>
        /// The Payment Method executes the payment Authorisation 
        /// for the received transactionpayment amount from the kiosk
        /// Sends socket inputs to smartpay
        /// Sends the Authorisation response value out
        /// if Authorisation is successful waits for an order number
        /// if Order number is valid from a transaction thats goes through
        /// run the settlement process or if transaction result not valid
        /// void the transaction.
        /// sends out the transaction reciepts to the payment service
        /// </summary>
        /// <param name="amount"></param>
        /// <param name="transactionRef"></param>
        /// <param name="transactionReceipts"></param>
        /// <returns></returns>
        public DiagnosticErrMsg Pay(int amount, string transactionRef, out TransactionReceipts transactionReceipts)
        {

            XDocument paymentXml = null;
            XDocument procTranXML = null;
            XDocument customerSuccessXML = null;
            XDocument processTransRespSuccessXML = null;
            XDocument finaliseXml = null;
            XDocument voidXml = null;
            XDocument finaliseSettleXml = null;
            XDocument paymentSettlementXml = null;
            XDocument procSettleTranXML = null;

            int intAmount;

            //check for a success or failure string from smartpay
            string submitPaymentResult = string.Empty;
            string finaliseResult = string.Empty;
            string finaliseSettleResult = string.Empty;
            string submitSettlePaymentResult = string.Empty;
            string description = transactionRef;

            // increment transaction number
            number++;
            transNum = number.ToString().PadLeft(6, '0');

            //check for transNum max value
            if (transNum == transactionLimit)
            {
                //reset back to beginning
                number = 1;
                transNum = number.ToString().PadLeft(6, '0');
            }

            transactionReceipts = new TransactionReceipts();

            //check amount is valid
            intAmount = Utils.GetNumericAmountValue(amount);

            if (intAmount == 0)
            {
                throw new Exception("Error in Amount value...");
            }

            Log.Info($"Valid payment amount: {intAmount}");
            Log.Info("Transaction Number : " +  transNum );
            Log.Info("Transaction Ref (Description) : " + transactionRef);

            /*********************** AUTHORISATION SECTION ***************************
            *                                                                       
            * Submittal – Submitting data to Smartpay Connect ready for processing. 
            * SUBMIT PAYMENT Process           
            * 
            *************************************************************************/

            //process Payment XML
            paymentXml = Payment(amount, transNum, description);

            Socket paymentSocket = CreateSocket();
            Log.Info("paymentSocket Open: " + SocketConnected(paymentSocket));

            //send submitpayment to smartpay - check response
            string paymentResponseStr = sendToSmartPay(paymentSocket, paymentXml, "SUBMITPAYMENT");

            //check response from Smartpay is not Null or Empty
            if (CheckIsNullOrEmpty(paymentResponseStr, "Submit Authorisation Payment")) isSuccessful = DiagnosticErrMsg.NOTOK;
            else
            {
                //check response outcome
                submitPaymentResult = CheckResult(paymentResponseStr);

                if (submitPaymentResult.ToLower() == "success")
                {
                    Log.Info("Successful payment submitted");
                }
                else
                {
                    Log.Error("Payment failed");
                    isSuccessful = DiagnosticErrMsg.NOTOK;
                }
            }
          
            //checkSocket closed
            Log.Info("paymentSocket Open: " + SocketConnected(paymentSocket));

            /************************************************************************************
            *                                                                                   *
            * Transactional – Processing of a transaction submitted during the submittal phase. *
            * PROCESSTRANSACTION process   - gets the Merchant receipt                          *
            *                                                                                   *
            *************************************************************************************/

            //create processtransaction socket
            Socket processSocket = CreateSocket();

            Log.Info("ProcessTransaction Socket Open: " + SocketConnected(processSocket));
   
            //Process Transaction XML
            procTranXML = processTransaction(transNum);

            //send processTransaction - check response
            string processTranReturnStr = sendToSmartPay(processSocket, procTranXML, "PROCESSTRANSACTION");

            //check response from Smartpay is not NULL or Empty
            if (CheckIsNullOrEmpty(processTranReturnStr, "Process Transaction")) isSuccessful = DiagnosticErrMsg.NOTOK;
            else
            {
                //check that the response contains a Receipt or is not NULL this is the Merchant receipt
                transactionReceipts.MerchantReturnedReceipt = ExtractXMLReceiptDetails(processTranReturnStr);

                //Check the merchant receipt is populated
                if (CheckIsNullOrEmpty(transactionReceipts.MerchantReturnedReceipt, "Merchant Receipt populated")) isSuccessful = DiagnosticErrMsg.NOTOK;
                else
                {
                    //check if reciept has a successful transaction
                    if (transactionReceipts.MerchantReturnedReceipt.Contains("DECLINED"))
                    {
                        Log.Error("Merchant Receipt has Declined Transaction.");
                        isSuccessful = DiagnosticErrMsg.NOTOK;
                    }
                }
            }         

            //check socket closed
            Log.Info("ProcessTransaction Socket Open: " + SocketConnected(processSocket));

            /******************************************************************************
            *                                                                             *
            * Interaction – Specific functionality for controlling POS and PED behaviour. *
            * gets the Customer receipt                                                   *
            *                                                                      *
            *******************************************************************************/

           //create customer socket
           Socket customerSuccessSocket = CreateSocket();
         
           Log.Info("customerSuccess Socket Open: " + SocketConnected(customerSuccessSocket));

            //process customerSuccess XML
           customerSuccessXML = PrintReciptResponse(transNum);

           string customerResultStr = sendToSmartPay(customerSuccessSocket, customerSuccessXML, "CUSTOMERECEIPT");

            //Check response from Smartpay is not Null or Empty
            if (CheckIsNullOrEmpty(customerResultStr, "Customer Receipt process")) isSuccessful = DiagnosticErrMsg.NOTOK;
            else
            {
                transactionReceipts.CustomerReturnedReceipt = ExtractXMLReceiptDetails(customerResultStr);

                //check returned receipt is not Null or Empty
                if (CheckIsNullOrEmpty(transactionReceipts.CustomerReturnedReceipt, "Customer Receipt returned")) isSuccessful = DiagnosticErrMsg.NOTOK;
                else
                {
                    //check if reciept has a successful transaction
                    if (transactionReceipts.CustomerReturnedReceipt.Contains("DECLINED"))
                    {
                        Log.Error("Customer Receipt has Declined Transaction.");
                        isSuccessful = DiagnosticErrMsg.NOTOK;
                    }
                }
            }

            Log.Info("customerSuccess Socket Open: " + SocketConnected(customerSuccessSocket));

            /***********************************************************************************************************
            *                                                                                                           
            * Interaction – Specific functionality for controlling PoS and PED behaviour. ( ProcessTransactionResponse)  
            * PROCESSTRANSACTIONRESPONSE                                                                                            
            *************************************************************************************************************/

            Socket processTransactionRespSocket = CreateSocket();
        
           Log.Info("processTransactionRespSocket Socket Open: " + SocketConnected(processTransactionRespSocket));
           processTransRespSuccessXML = PrintReciptResponse(transNum);
        
            string processTransRespStr = sendToSmartPay(processTransactionRespSocket, processTransRespSuccessXML, "PROCESSTRANSACTIONRESPONSE");

            //check response from Smartpay is not Null or Empty
            if (CheckIsNullOrEmpty(processTransRespStr, "Process Transaction Response")) isSuccessful = DiagnosticErrMsg.NOTOK;
            else
            {
                //get the reference value from the process Transaction Response
                // this is needed for the settlement process
                //
                reference = GetReferenceValue(processTransRespStr);

                Log.Info($"REFERENCE Number = {reference}");


                if (processTransRespStr.Contains("declined"))
                {
                    Log.Error("***** Auth Process Transaction Response has Declined Transaction. *****");
                    isSuccessful = DiagnosticErrMsg.NOTOK;
                }
            }

            Log.Info("processTransRespSuccessXML Socket Open: " + SocketConnected(processTransactionRespSocket));

          

            /*****************************************************************************************************************
             *                                                                                                               
             * finalise Response message so that the transaction can be finalised and removed from Smartpay Connect's memory 
             *   
             *   FINALISE                                                                                                           
            ******************************************************************************************************************/

            Socket finaliseSocket = CreateSocket();
            Log.Info("Finalise Socket Open: " + SocketConnected(finaliseSocket));

            finaliseXml = Finalise(transNum);
      
            string finaliseStr = sendToSmartPay(finaliseSocket, finaliseXml, "FINALISE");

            //check response from Smartpay is not Null or Empty
            if (CheckIsNullOrEmpty(finaliseStr, "Finalise Authorisation")) isSuccessful = DiagnosticErrMsg.NOTOK;
            else
            {
                finaliseResult = CheckResult(finaliseStr);

                if (finaliseResult == "success")
                {
                    Log.Info("****** Authorisation Transaction Finalised Successfully******");
                }
                else
                {
                    Log.Info("***** Authorisation Transaction not Finalised *****");
                    isSuccessful = DiagnosticErrMsg.NOTOK;
                }
            }
            

           Log.Info("Finalise Socket Open: " + SocketConnected(finaliseSocket));

            // check if the Authorisation has been successful
            if (isSuccessful == DiagnosticErrMsg.OK)
                authorisationFlag = true;
            else
                authorisationFlag = false;
  
            WriteDataToFile(authorisationFlag);

            Log.Info($"Authorisation result {authorisationFlag} saved to file");

            if (authorisationFlag == false)
            {
                //authoriation has failed return to paymentService
                //end payment process and  print out failure receipt.

                Log.Error("\n***** Authorisation Check has failed. *****\n");
                return isSuccessful;
            }
            else
            {
                Log.Info("\n***** Payment Authorisation Check has passed. Wait for Order Number. *****\n");
            }

            /*************************************************************************************************
            * SIMULATION
            *
            * if authorisation check has passed pass the authorisation result to the database
            * and wait for an order response from the transaction
            * (will use a file to simulate this response)
            * 
            * *************************************************************************************************/

            string orderNumResponse = string.Empty;


            //wait 10 seconds for reply from payment
            Thread.Sleep(10000);

            // check return from transaction
            orderNumResponse = ReadResponseFile();

            Log.Info("Order number returned = " + orderNumResponse);

            /********************************************************************************
             * 
             * Submittal using settlement Reference                                 
             *   
             * 
             * 
             * 
             * ******************************************************************************/

            if ((isSuccessful == DiagnosticErrMsg.OK) && (authorisationFlag == true) && (!(string.IsNullOrEmpty(orderNumResponse))))
            {
                /****************************************************************************
                 * Submittal using settlement Reference, amount , transNum
                 * description which is the transaction reference
                 * 
                 * Submit Settlement Payment                                                           *
                 ****************************************************************************/
                Log.Info("\n******Performing Settlement Payment ******\n");

                paymentSettlementXml = PaymentSettle(amount, transNum, reference, description);

                // open paymentXml socket connection
                Socket paymenSettlementSocket = CreateSocket();

                //check socket open
                Log.Info("Paymentsocket Open: " + SocketConnected(paymenSettlementSocket));

                //send submitpayment to smartpay - check response
                string paymentSettleResponseStr = sendToSmartPay(paymenSettlementSocket, paymentSettlementXml, "SUBMITPAYMENT");
               

                //check response from Smartpay is not Null or Empty
                if (CheckIsNullOrEmpty(paymentSettleResponseStr, "Settlement Payment")) isSuccessful = DiagnosticErrMsg.NOTOK;
                else
                {
                    submitSettlePaymentResult = CheckResult(paymentSettleResponseStr);

                    if (submitSettlePaymentResult == "success")
                    {
                        Log.Info("******Successful Settlement Payment submitted******\n");
                    }
                    else
                    {
                        Log.Error("****** Settlement Payment failed******\n");
                        isSuccessful = DiagnosticErrMsg.NOTOK;
                    }
                }
               

                Log.Info("paymenSettlementSocket Open: " + SocketConnected(paymenSettlementSocket));

                /****************************************************************************
                * Procees the settlement transaction                                        *
                *                                                                           *
                *****************************************************************************/
                Socket processSettleSocket = CreateSocket();

                Log.Info("processSettleSocket Socket Open: " + SocketConnected(processSettleSocket));

                procSettleTranXML = processTransaction(transNum);
                //send processTransaction - check response

                string processSettleTranResponseStr = sendToSmartPay(processSettleSocket, procSettleTranXML, "PROCESSSETTLETRANSACTION");
                //check response from Smartpay is not Null or Empty
                if (CheckIsNullOrEmpty(processSettleTranResponseStr, "Process Settlement Transaction")) isSuccessful = DiagnosticErrMsg.NOTOK;
                //No response needed

                Log.Info("processSettleSocket Socket Open: " + SocketConnected(processSettleSocket));

                /****************************************************************************
                 * Procees the Settlement finalise transaction                                        *
                 *                                                                           *
                 *****************************************************************************/
                Socket finaliseSettSocket = CreateSocket();

                Log.Info("Finalise Socket Open: " + SocketConnected(finaliseSettSocket));
                finaliseSettleXml = Finalise(transNum);

                //check response
                string finaliseSettleStr = sendToSmartPay(finaliseSettSocket, finaliseSettleXml, "FINALISE");
                if (CheckIsNullOrEmpty(finaliseSettleStr, "Settlement Finalise")) isSuccessful = DiagnosticErrMsg.NOTOK;
                else
                {
                    finaliseSettleResult = CheckResult(finaliseSettleStr);

                    if (finaliseSettleResult == "success")
                    {
                        Log.Info("******Transaction Settle  Finalised successfully******\n");
                    }
                    else
                    {
                        Log.Error("****** Transaction Settle not Finalised ******\n");
                        isSuccessful = DiagnosticErrMsg.NOTOK;
                    }

                }
              

                Log.Info("Finalise Socket Open: " + SocketConnected(finaliseSettSocket));
            }
            else
            {
                ////////////////////////
                // void the transaction
                ////////////////////////

                Socket voidSocket = CreateSocket();
                Log.Info("void Socket Open: " + SocketConnected(voidSocket));

                voidXml = voidTransaction(transNum, transactionRef);
                string voidResponseStr = sendToSmartPay(voidSocket, voidXml, "VOID");


                //success is not OK
                isSuccessful = DiagnosticErrMsg.NOTOK;
                Log.Info("void Socket Open: " + SocketConnected(voidSocket));
            }

            return isSuccessful;

        }

        public bool CheckIsNullOrEmpty(string stringToCheck, string stringCheck)
        {
            if (string.IsNullOrEmpty(stringToCheck))
            {
                Log.Error($" String check: {stringCheck} returned a Null or Empty value.");
                isSuccessful = DiagnosticErrMsg.NOTOK;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Sends XML document for each operation for smartpay to process
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="operation"></param>
        /// <param name="operationStr"></param>
        /// <returns></returns>
        private string sendToSmartPay(Socket sender, XDocument operation, string operationStr)
        {
            int bytesRec = 0;
            string message = string.Empty;

            // Connect the socket to the remote endpoint. Catch any errors.  
            try
            {
                sender.Connect(remoteEP);

                // Encode the data string into a byte array.  
                byte[] msg = Encoding.ASCII.GetBytes(operation.ToString());

                // Send the data through the socket.  
                int bytesSent = sender.Send(msg);

                if ((operationStr == "PROCESSSETTLETRANSACTION"))
                {
                    do
                    {
                        bytesRec = sender.Receive(bytes);
                        message = Encoding.ASCII.GetString(bytes, 0, bytesRec);
                       // Log.Info($"{operationStr} is {Encoding.ASCII.GetString(bytes, 0, bytesRec)}");


                        if (message.Contains("processTransactionResponse"))
                        {
                            Log.Info("************ Processs Settlement transaction response  received *************");
                            return message;
                        }

                    } while (message != string.Empty);

                }

                if ((operationStr == "PROCESSTRANSACTION") || (operationStr == "CUSTOMERECEIPT"))
                {
                    do
                    {
                        bytesRec = sender.Receive(bytes);
                        message = Encoding.ASCII.GetString(bytes, 0, bytesRec);
                       // Log.Info($"{operationStr} is {Encoding.ASCII.GetString(bytes, 0, bytesRec)}");

                        //Check for a receipt - don't want to process display messages
                        if (message.Contains("posPrintReceipt")) return message;

                    } while (message.Contains("posDisplayMessage"));

                }
                if ((operationStr == "SUBMITPAYMENT") || (operationStr == "FINALISE"))
                {
                    do
                    {
                        // Receive the response from the remote device and check return
                        bytesRec = sender.Receive(bytes);
                        if (bytesRec != 0)
                        {
                            //  Log.Info($"{operationStr} is {Encoding.ASCII.GetString(bytes, 0, bytesRec)}");
                            return Encoding.ASCII.GetString(bytes, 0, bytesRec);
                        }

                    } while (bytesRec != 0);
                }

                if (operationStr == "PROCESSTRANSACTIONRESPONSE")
                {
                    do
                    {
                        bytesRec = sender.Receive(bytes);
                        message = Encoding.ASCII.GetString(bytes, 0, bytesRec);
                       // Log.Info($"operationStr is {message}");


                        if (message.Contains("processTransactionResponse"))
                        {
                            Log.Info("**** Processs transaction Called ****");
                            return message;
                        }


                    } while (message != string.Empty);

                }

                if ((operationStr == "VOID"))
                {

                    bytesRec = sender.Receive(bytes);
                    message = Encoding.ASCII.GetString(bytes, 0, bytesRec);
                    Console.WriteLine($"{operationStr} is {message}");


                    if (message.Contains("CANCELLED"))
                    {
                        Log.Info("****** Transaction VOID  successful *****");
                        
                        return message;
                    }

                }

            }
            catch (ArgumentNullException ane)
            {
               Log.Error("ArgumentNullException : {0}", ane.ToString());
            }
            catch (SocketException se)
            {
               Log.Error("SocketException : {0}", se.ToString());
            }
            catch (Exception e)
            {
               Log.Error("Unexpected exception : {0}", e.ToString());
            }

            return string.Empty;
        }

        /// <summary>
        /// Checks a string for a success or failure string
        /// </summary>
        /// <param name="submitResult"></param>
        /// <returns></returns>
        private string CheckResult(string submitResult)
        {
            string result = string.Empty;
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(submitResult);
            XmlNodeList nodeResult = doc.GetElementsByTagName("RESULT");

            for (int i = 0; i < nodeResult.Count; i++)
            {
                if (nodeResult[i].InnerXml == "success")
                    result = "success";
                else
                    result = "failure";
            }

            return result;
        }


        /// <summary>
        /// Gets the reference number from a string
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private string GetReferenceValue(string message)
        {
            string reference = message.Substring(message.IndexOf(toBeSearched) + toBeSearched.Length);
            StringBuilder str = new StringBuilder();

            // get everything up until the first whitespace
            int num = reference.IndexOf("date");
            reference = reference.Substring(1, num - 3);

            return reference;
        }

        public XDocument Payment(int amount, string transNum, string description)
        {
            XDocument payment = XDocument.Parse(
                                  "<RLSOLVE_MSG version=\"5.0\">" +
                                  "<MESSAGE>" +
                                 "<SOURCE_ID>" + sourceId + "</SOURCE_ID>" +
                                  "<TRANS_NUM>" + transNum +
                                  "</TRANS_NUM>" +
                                  "</MESSAGE>" +
                                  "<POI_MSG type=\"submittal\">" +
                                   "<SUBMIT name=\"submitPayment\">" +
                                    "<TRANSACTION type= \"purchase\" action =\"auth\" source =\"icc\" customer=\"present\">" +
                                    "<AMOUNT currency=\"" + currency + "\" country=\"" + country + "\">" +
                                      "<TOTAL>" + amount + "</TOTAL>" +
                                    "</AMOUNT>" +
                                    "<DESCRIPTION>" + description + "</DESCRIPTION>" +
                                    "</TRANSACTION>" +
                                   "</SUBMIT>" +
                                  "</POI_MSG>" +
                                "</RLSOLVE_MSG>");

            return payment;
        }

        public XDocument PaymentSettle(int amount, string transNum, string reference, string description)
        {
            XDocument paymentSettle = XDocument.Parse(
                                  "<RLSOLVE_MSG version=\"5.0\">" +
                                  "<MESSAGE>" +
                                  "<TRANS_NUM>" + transNum +
                                  "</TRANS_NUM>" +
                                  "</MESSAGE>" +
                                  "<POI_MSG type=\"submittal\">" +
                                   "<SUBMIT name=\"submitPayment\">" +
                                    "<TRANSACTION type= \"purchase\" action =\"settle_transref\" source =\"icc\" customer=\"present\" reference= "
                                    + "\"" + reference + "\"" + "> " +
                                     "<AMOUNT currency=\"" + currency + "\" country=\"" + country + "\">" +
                                      "<TOTAL>" + amount + "</TOTAL>" +
                                    "</AMOUNT>" +
                                    "</TRANSACTION>" +
                                   "</SUBMIT>" +
                                  "</POI_MSG>" +
                                "</RLSOLVE_MSG>");

            return paymentSettle;
        }

        public XDocument processTransaction(string transNum)
        {
            XDocument processTran = XDocument.Parse(
                              "<RLSOLVE_MSG version=\"5.0\">" +
                              "<MESSAGE>" +
                                "<TRANS_NUM>" +
                                    transNum +
                                "</TRANS_NUM>" +
                              "</MESSAGE>" +
                              "<POI_MSG type=\"transactional\">" +
                              "<TRANS name=\"processTransaction\"></TRANS>" +
                              "</POI_MSG>" +
                            "</RLSOLVE_MSG>");

            return processTran;

        }


        public XDocument PrintReciptResponse(string transNum)
        {
            XDocument printReceipt = XDocument.Parse(
                            "<RLSOLVE_MSG version=\"5.0\">" +
                            "<MESSAGE>" +
                              "<TRANS_NUM>" +
                                  transNum +
                              "</TRANS_NUM>" +
                            "</MESSAGE>" +
                            "<POI_MSG type=\"interaction\">" +
                              "<INTERACTION name=\"posPrintReceiptResponse\">" +
                                  "<RESPONSE>success</RESPONSE>" +
                              "</INTERACTION>" +
                            "</POI_MSG>" +
                          "</RLSOLVE_MSG>");

            return printReceipt;
        }


        public XDocument Finalise(string transNum)
        {
            XDocument finalise = XDocument.Parse(
                            "<RLSOLVE_MSG version=\"5.0\">" +
                            "<MESSAGE>" +
                              "<TRANS_NUM>" +
                                  transNum +
                              "</TRANS_NUM>" +
                            "</MESSAGE>" +
                            "<POI_MSG type=\"transactional\">" +
                             "<TRANS name=\"finalise\"></TRANS>" +
                            "</POI_MSG>" +
                          "</RLSOLVE_MSG>");


            return finalise;
        }

        public XDocument voidTransaction(string transNum, string transRef)
        {
            XDocument cancel = XDocument.Parse(
                            "<RLSOLVE_MSG version=\"5.0\">" +
                            "<MESSAGE>" +
                                "<TRANS_NUM>" +
                                  transNum +
                              "</TRANS_NUM>" +
                             "<SOURCE_ID>" + sourceId + "</SOURCE_ID>" +
                            "</MESSAGE>" +
                            "<POI_MSG type=\"administrative\">" +
                             "<ADMIN name=\"voidTransaction\">" +
                              "<TRANSACTION reference =\"" + transRef + "\"></TRANSACTION>" +
                             "</ADMIN>" +
                            "</POI_MSG>" +
                          "</RLSOLVE_MSG>");


            return cancel;
        }

        
        private Socket CreateSocket()
        {
            // Create a TCP/IP  socket.  
            Socket sender = new Socket(ipAddress.AddressFamily,
                SocketType.Stream, ProtocolType.Tcp);

            return sender;
        }



        bool SocketConnected(Socket s)
        {
            bool part1 = s.Poll(1000, SelectMode.SelectRead);
            bool part2 = (s.Available == 0);
            if (part1 && part2)
                return false;
            else
                return true;
        }


        string ExtractXMLReceiptDetails(string receiptStr)
        {
            string returnedStr = string.Empty;

            var receiptDoc = new XmlDocument();
            receiptDoc.LoadXml(receiptStr);

            returnedStr = receiptDoc.GetElementsByTagName("RECEIPT")[0].InnerText;

            return returnedStr;
        }


            public void WriteDataToFile(bool authorisationFlag)
            {
                using (TextWriter writer = File.CreateText("C:/Customer Payment Drivers/SmartPay/Smartpay.txt"))
                {
                    writer.WriteLine(authorisationFlag);
                }
            }


            public string ReadResponseFile()
            {
                string line = string.Empty;

                using (TextReader reader = File.OpenText("C:/Customer Payment Drivers/SmartPay/SmartpayResponse.txt"))
                {
                    //read line if it is empty
                    line = reader.ReadLine();
                    Console.WriteLine(line);

                    if (line != string.Empty)
                    {
                     paymentSuccessful = line;
                    }
                }

                return line;

            }

            public void Dispose() {}
    }
}
