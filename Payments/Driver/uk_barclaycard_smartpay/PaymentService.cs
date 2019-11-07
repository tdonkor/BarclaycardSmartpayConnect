using Acrelec.Library.Logger;
using Acrelec.Mockingbird.Payment.Configuration;
using Acrelec.Mockingbird.Payment.Contracts;
using Acrelec.Mockingbird.Feather.Peripherals.Payment.Model;
using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.ServiceModel;


namespace Acrelec.Mockingbird.Payment
{

    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall)]
    public class PaymentService : IPaymentService
    {
        private static readonly string ticketPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "ticket");
        private string transactionRef = string.Empty;

        /// <summary>
        /// Get the configuratiion data
        /// </summary>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public Result Init(RuntimeConfiguration configuration)
        {
            Log.Info("Init method started...");

            //initalise confguration file instance
            var configFile = AppConfiguration.Instance;

            try
            {
                if (configuration == null)
                {
                    Log.Info("Can not set configuration to null.");
                    return ResultCode.GenericError;
                }

                if (configuration.PosNumber <= 0)
                {
                    Log.Info($"Invalid PosNumber {configuration.PosNumber}.");
                    return ResultCode.GenericError;
                }

                using (var api = new BarclayCardSmartpayApi())
                {
                    RuntimeConfiguration.Instance = configuration;
                    Heartbeat.Instance.Start();
                    Log.Info("Init success!");

                    return ResultCode.Success;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return ResultCode.GenericError;
            }
            finally
            {
                Log.Info("Init method finished.");
            }
        }

        /// <summary>
        /// Test HeartBeat
        /// </summary>
        /// <returns></returns>
        public Result Test()
        {
            var alive = Heartbeat.Instance?.Alive == true;
            Log.Debug($"Test status: {alive}");
            return alive ? ResultCode.Success : ResultCode.GenericError;
        }
 
            /// <summary>
            /// Payment method
            /// </summary>
            /// <param name="amount"></param>
            /// <returns></returns>
            public Result<PaymentData> Pay(int amount, string transactionRef)
            {
                Log.Info("Payment method started...");
                Log.Info($"Amount = {amount / 100.0}.");
                Log.Info($"Transaction Reference  = {transactionRef}.");
                Result<PaymentData> transactionResult = null;
                string reciept = string.Empty;

            try
            {
                if (File.Exists(ticketPath))
                {
                    File.Delete(ticketPath);
                }

                if (amount <= 0)
                {
                    Log.Info("Invalid pay amount...");
                    return ResultCode.GenericError;
                }

                if (transactionRef == string.Empty)
                {
                    Log.Info("Transaction reference is empty");
                    return ResultCode.GenericError;
                }
        
                var config = RuntimeConfiguration.Instance;
                var data = new PaymentData();
               

                Log.Info("Calling payment driver...");

                using (var api = new BarclayCardSmartpayApi())
                {
                 
                    var payResult = api.Pay(amount, transactionRef, out TransactionReceipts payReceipts);
                    Log.Info($"Pay Result: {payResult}");


                    // interogate the result check if payReceipts not equal to null

                    //if (payReceipts == null)
                    //{
                    //    Log.Error("Transaction response error...");
                    //    data.Result = PaymentResult.Failed;
                    //    PrintErrorTicket(data);
                    //    return new Result<PaymentData>((ResultCode)payResult, data: data);

                    //}
              
                    if (payResult != DiagnosticErrMsg.OK)
                    {
                        Log.Error($"Pay Result = {payResult} Payment Failed...See Stored Ticket and Logs ");
                        data.Result = PaymentResult.Failed;

                        PrintErrorTicket(data);

                        return new Result<PaymentData>((ResultCode)payResult, data: data);
                    }
                    else

                    if (payResult == DiagnosticErrMsg.OK)
                    {
                        data.Result = PaymentResult.Successful;
                        data.PaidAmount = amount;

                        Log.Info($"paid Amount: {data.PaidAmount}");
                        transactionResult = new Result<PaymentData>(ResultCode.Success, data: data);
                        Log.Info($"Payment succeeded transaction result: {transactionResult}");


                        //persist the Merchant transaction
                        PersistTransaction(payReceipts.MerchantReturnedReceipt, "MERCHANT");

                        CreateTicket(payReceipts.CustomerReturnedReceipt, "CUSTOMER");

                        data.HasClientReceipt = true;
                        data.HasMerchantReceipt = true;
                    }
                }


                return transactionResult;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return ResultCode.GenericError;
            }
            finally
            {
                Log.Info("Pay method finished...");
            }
        }

        private static void PrintErrorTicket(PaymentData data)
        {
            //print the payment ticket for an error
            //
            CreateTicket("\nPayment failure with\nyour card or issuer\nNO payment has been taken.\n\nPlease try again with another card,\nor at a manned till.\n\n", "Error");
            data.HasClientReceipt = true;
            data.HasMerchantReceipt = true;
        }

        /// <summary>
        /// Shutdown
        /// </summary>
        public void Shutdown()
        {
            Log.Info("Shutting down...");
            Program.ManualResetEvent.Set();
        }

        /// <summary>
        /// Persist the transaction as Text file
        /// with Customer and Merchant receiept
        /// </summary>
        /// <param name="result"></param>
        private static void PersistTransaction(string receipt, string ticketType)
        {
            try
            {
                var config = AppConfiguration.Instance;
                var outputDirectory = Path.GetFullPath(config.OutPath);
                var outputPath = Path.Combine(outputDirectory, $"{DateTime.Now:yyyyMMddHHmmss}_{ticketType}_ticket.txt");

                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                Log.Info($"Persisting {ticketType} to {outputPath}" );

                //Write the new ticket
                File.WriteAllText(outputPath, receipt.ToString());
            }
            catch (Exception ex)
            {
                Log.Error("Persist Transaction exception.");
                Log.Error(ex);
            }
        }



        private static void CreateTicket(string ticket, string ticketType)
        {
            try
            {
                Log.Info($"Persisting {ticketType} to {ticketPath}");

                //Write the new ticket
                File.WriteAllText(ticketPath, ticket);

                //persist the transaction
                PersistTransaction(ticket, ticketType);

            }
            catch (Exception ex)
            {
                Log.Error($"Error {ticketType} persisting ticket.");
                Log.Error(ex);
            }
        }
    }
}

