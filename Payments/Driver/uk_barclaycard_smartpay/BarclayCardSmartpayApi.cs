using Acrelec.Library.Logger;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Acrelec.Mockingbird.Payment
{
    public class BarclayCardSmartpayApi : IDisposable
    {

        //int amount
        int port = 8000;
        IPHostEntry ipHostInfo;
        IPAddress ipAddress;
        IPEndPoint remoteEP;
        DiagnosticErrMsg isSuccessful = DiagnosticErrMsg.OK;

        // Data buffer for incoming data.
        byte[] bytes = new byte[1024];

  
        /// <summary>
        /// Constructor
        /// </summary>
        public BarclayCardSmartpayApi()
        {
            // Establish the remote endpoint for the socket.  
            // This example uses port 8000 on the local computer.  
            ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            ipAddress = ipHostInfo.AddressList[0];
            remoteEP = new IPEndPoint(ipAddress, port);
        }

        public DiagnosticErrMsg Pay(int amount, string transactionRef, out TransactionReceipts transactionReceipts)
        {

            XDocument paymentXml = null;
            XDocument procTranXML = null;
            XDocument firstInteractionXML = null;
            XDocument secondInteractionXML = null;
            XDocument FinaliseXml = null;

            int intAmount;
            
             

            transactionReceipts = new TransactionReceipts();

            //check amount is valid
            intAmount = Utils.GetNumericAmountValue(amount);

            if (intAmount == 0)
            {
                throw new Exception("Error in Amount value...");
            }

            Log.Info($"Valid payment amount: {intAmount}");

            //transNum = transactionRef;

            //check for a success or failure string  in return
            string submitPaymentResultStr = string.Empty;
            string finaliseResultStr = string.Empty;
            string receiptResultStr = string.Empty;


            //string transNum = string.Empty;

            Random rnd = new Random();
             int transNum = rnd.Next(1, int.MaxValue);
            //int transNum = Convert.ToInt32(transactionRef);
           // transNum = transactionRef;

            Log.Info("Transaction Number is ***** " + transNum + " *****\n\n");

            //************ PROCEDURES ***********

            /************************************************************************
            * Submittal – Submitting data to Smartpay Connect ready for processing. *
            * payment                                                               *
            *************************************************************************/
            paymentXml = Payment(amount, transNum);

            Socket paymentsocket = CreateSocket();
            Log.Info("Paymentsocket Open: " + SocketConnected(paymentsocket));

            //send submitpayment to smartpay - check response
            string paymentResponseStr = sendToSmartPay(paymentsocket, paymentXml, "PAYMENT");

            submitPaymentResultStr = CheckResult(paymentResponseStr);

            if (submitPaymentResultStr == "success")
            {
                Log.Info("Successful payment submitted");
            }
            else
            {
                Log.Error("Payment failed");
                isSuccessful = DiagnosticErrMsg.NOTOK;
            }

            //checkSocket closed
            Log.Info("Paymentsocket Open: " + SocketConnected(paymentsocket));

            /************************************************************************************
            * Transactional – Processing of a transaction submitted during the submittal phase. *
            * Process Transaction   - gets the Merchant receipt                                                          *
            *************************************************************************************/

            Socket processSocket = CreateSocket();

            Log.Info("ProcessTransaction Socket Open: " + SocketConnected(processSocket));
   
            procTranXML = processTransaction(transNum);

            string processTranStr = sendToSmartPay(processSocket, procTranXML, "PROCESSTRANSACTION");

            //check that the response contains a Receipt or is not NULL this is the Merchant receipt
            //
            transactionReceipts.MerchantReturnedReceipt =   ExtractXMLReceiptDetails(processTranStr);

            //Check the merchant receipt is populated
            if (transactionReceipts.MerchantReturnedReceipt == string.Empty)
            {
                Log.Error("No Merchant receipt returned");
                isSuccessful = DiagnosticErrMsg.NOTOK;
            }
            else
            {
                //check if reciept has a successful transaction
                if (transactionReceipts.MerchantReturnedReceipt.Contains("DECLINED"))
                {
                    Log.Error("Merchant Receipt has Declined Transaction.");
                    isSuccessful = DiagnosticErrMsg.NOTOK;
                }
            }

            Log.Info("ProcessTransaction Open: " + SocketConnected(paymentsocket));

            //TODO if receipt is not successful cancel the transaction or send the successful response
            
            /*****************************************************************************
            * Interaction – Specific functionality for controlling PoS and PED behaviour.*
            * gets the Customer receipt                                                  *
            *******************************************************************************/

           Socket firstInteractionSocket = CreateSocket();
         
           Log.Info("firstInteractionXML Socket Open: " + SocketConnected(firstInteractionSocket));

           firstInteractionXML = PrintReciptResponse(transNum);

           string firstInteractionStr = sendToSmartPay(firstInteractionSocket, firstInteractionXML, "GETRECEIPTS");

           Log.Info($"firstInteractionStr Return: {firstInteractionStr}");

            transactionReceipts.CustomerReturnedReceipt = ExtractXMLReceiptDetails(firstInteractionStr);

            if (string.IsNullOrEmpty(transactionReceipts.CustomerReturnedReceipt))
            {
                Log.Error("No Customer receipt returned");
                isSuccessful = DiagnosticErrMsg.NOTOK;
            }
            else
            {
                //check if reciept has a successful transaction
                if (transactionReceipts.CustomerReturnedReceipt.Contains("DECLINED"))
                {
                    Log.Error("Customer Receipt has Declined Transaction.");
                    isSuccessful = DiagnosticErrMsg.NOTOK;
                }
            }

            Log.Info("firstInteractionXML Socket Open: " + SocketConnected(firstInteractionSocket));

            /***********************************************************************************************************
            * Interaction – Specific functionality for controlling PoS and PED behaviour. ( ProcessTransactionResponse) *                                                                      *
             ************************************************************************************************************/

           Socket secondInteractionSocket = CreateSocket();
        
           Log.Info("secondInteractionSocket Socket Open: " + SocketConnected(secondInteractionSocket));
           secondInteractionXML = PrintReciptResponse(transNum);
        
            string secondInteractionStr = sendToSmartPay(secondInteractionSocket, secondInteractionXML, "PROCESSTRANRESPONSE");

           Log.Info($"secondInteractionStr Return: {secondInteractionStr}");

            if (secondInteractionStr.Contains("declined"))
            {
                Log.Error("Process Transaction Response has Declined Transaction.");
                isSuccessful = DiagnosticErrMsg.NOTOK;
            }

            Log.Info("secondInteractionXML Socket Open: " + SocketConnected(secondInteractionSocket));

            /****************************************************************************************************************
             * finalise Response message so that the transaction can be finalised and removed from Smartpay Connect's memory *
            ****************************************************************************************************************/

            Socket finaliseSocket = CreateSocket();
            Log.Info("Finalise Socket Open: " + SocketConnected(finaliseSocket));

            FinaliseXml = Finalise(transNum);
      
            string finaliseStr = sendToSmartPay(finaliseSocket, FinaliseXml, "FINALISE");
            finaliseResultStr = CheckResult(finaliseStr);

            if (finaliseResultStr == "success")Log.Info("******Transaction Finalised successfully******\n");
            else
               Log.Info("****** Transaction not Finalised ******\n");

           Log.Info("Finalise Socket Open: " + SocketConnected(finaliseSocket));

            Log.Info("Returning the value: " + isSuccessful);

            return isSuccessful;

        }

        /// <summary>
        /// 
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
                //Log.Info("Connection is active 1: " + SocketConnected(sender));

                // Encode the data string into a byte array.  
                byte[] msg = Encoding.ASCII.GetBytes(operation.ToString());

                // Send the data through the socket.  
                int bytesSent = sender.Send(msg);

                if ((operationStr == "PROCESSTRANSACTION") || (operationStr == "GETRECEIPTS"))
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
                if ((operationStr == "PAYMENT") || (operationStr == "FINALISE"))
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
             
                if (operationStr == "PROCESSTRANRESPONSE")
                {
                    do
                    {
                        bytesRec = sender.Receive(bytes);
                        message = Encoding.ASCII.GetString(bytes, 0, bytesRec);
                       // Log.Info($"operationStr is {message}");


                        if (message.Contains("processTransactionResponse"))
                        {
                            Log.Info("************ Processs transaction Called *************");
                            return message;
                        }


                    } while (message != string.Empty);


                }

            }
            catch (ArgumentNullException ane)
            {
               Log.Info("ArgumentNullException : {0}", ane.ToString());
            }
            catch (SocketException se)
            {
               Log.Info("SocketException : {0}", se.ToString());
            }
            catch (Exception e)
            {
               Log.Info("Unexpected exception : {0}", e.ToString());
            }

            return string.Empty;
        }

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


        public XDocument Payment(int amount, int transNum)
        {
            XDocument payment = XDocument.Parse(
                                  "<RLSOLVE_MSG version=\"5.0\">" +
                                  "<MESSAGE>" +
                                  "<SOURCE_ID>DK01ACRELEC</SOURCE_ID>" +
                                  "<TRANS_NUM>" + transNum +
                                  "</TRANS_NUM>" +
                                  "</MESSAGE>" +
                                  "<POI_MSG type=\"submittal\">" +
                                   "<SUBMIT name=\"submitPayment\">" +
                                    "<TRANSACTION type= \"purchase\" action =\"auth\" source =\"icc\" customer=\"present\">" +
                                    "<AMOUNT currency=\"826\" country=\"826\">" +
                                      "<TOTAL>" + amount + "</TOTAL>" +
                                    "</AMOUNT>" +
                                    "</TRANSACTION>" +
                                   "</SUBMIT>" +
                                  "</POI_MSG>" +
                                "</RLSOLVE_MSG>");

            return payment;
        }

        public XDocument processTransaction(int transNum)
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


        public XDocument PrintReciptResponse(int transNum)
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


        public XDocument Finalise(int transNum)
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

        //new 
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

        public void Dispose()
        {

        }
    }
}
