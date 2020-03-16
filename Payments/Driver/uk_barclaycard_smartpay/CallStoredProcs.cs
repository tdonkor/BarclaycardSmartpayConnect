using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Acrelec.Library.Logger;
using Newtonsoft.Json;
using RestSharp;

namespace Acrelec.Mockingbird.Payment
{
    public class CallStoredProcs
    {
     
        /// <summary>
        /// Get the Basket ID
        /// </summary>
        /// <param name="con"></param>
        /// <param name="apiOrderID"></param>
        /// <returns></returns>
        public int OrderBasket_Current_ByAPIOrderID(SqlConnection con, string apiOrderID)
        {
            SqlCommand com = con.CreateCommand();
            com.CommandType = CommandType.StoredProcedure;
            com.CommandText = "OrderBasket_Current_ByAPIOrderID";

            com.Parameters.Add("@APIOrderID", SqlDbType.VarChar).Value = apiOrderID;
            var result = com.ExecuteScalar();
            int orderBasketId = Convert.ToInt32(result.ToString());
            Log.Info("Execute OrderBasket_Current_ByAPIOrderID - OrderBasketID = {0}: ", orderBasketId);

            return orderBasketId;
        }


     /// <summary>
     /// Get Mark As Paid Payload
     /// </summary>
     /// <param name="con"></param>
     /// <param name="orderBasketID"></param>
     /// <returns></returns>
     public string ExecuteOrderBasket_API_MarkAsPaid(SqlConnection con, int orderBasketID)
     {
        string jsonPayload = "";
        // Create and configure a command object
        SqlCommand com = con.CreateCommand();
        com.CommandType = CommandType.StoredProcedure;
        com.CommandText = "OrderBasket_API_MarkAsPaid";
        com.Parameters.Add("@OrderBasketID", SqlDbType.Int).Value = orderBasketID;
        using (IDataReader reader = com.ExecuteReader())
        {
            while (reader.Read())
            {
               var result = (string)reader[0];
               if (result == "-1" || result == "0")
               {
                  Log.Error("Execute OrderBasket_API_MarkAsPaid - OrderBasketID = {0}: \nResult: {1}", orderBasketID, result);
               }
               else
               {
                   jsonPayload = result;
                   Log.Info("Execute OrderBasket_API_MarkAsPaid - OrderBasketID = {0}: \nPayload: {1}", orderBasketID,
                   jsonPayload);
               }
             }
        }
        return jsonPayload;
     }



        /// <summary>
        /// Check Mark As Paid response 
        /// </summary>
        /// <param name="con"></param>
        /// <param name="orderBasketID"></param>
        /// <param name="jsonResponse"></param>
        public  void ExecuteOrderBasket_APIResponse_MarkAsPaid(SqlConnection con, int orderBasketID, string jsonResponse)
        {
            // Create and configure a command object
            SqlCommand com = con.CreateCommand();
            com.CommandType = CommandType.StoredProcedure;
            com.CommandText = "OrderBasket_APIResponse_MarkAsPaid";
            com.Parameters.Add("@OrderBasketID", SqlDbType.Int).Value = orderBasketID;
            com.Parameters.Add("@json", SqlDbType.VarChar).Value = jsonResponse;
            var result = com.ExecuteScalar();
            Log.Info("Execute OrderBasket_APIResponse_MarkAsPaid: OrderBasketID {0}: \nBasketId: {1}", orderBasketID,
            result);
        }

        /// <summary>
        /// Get Send to POS payload
        /// </summary>
        /// <param name="con"></param>
        /// <param name="orderBasketID"></param>
        /// <returns></returns>
        public string ExecuteOrderBasket_API_SendToPos(SqlConnection con, int orderBasketID)
        {
            string jsonPayload = "";
            // Create and configure a command object
            SqlCommand com = con.CreateCommand();
            com.CommandType = CommandType.StoredProcedure;
            com.CommandText = "OrderBasket_API_SendToPos";
            com.Parameters.Add("@OrderBasketID", SqlDbType.Int).Value = orderBasketID;
            using (IDataReader reader = com.ExecuteReader())
            {
                while (reader.Read())
                {
                    var result = (string)reader[0];
                    if (result == "-1" || result == "0")
                    {
                        Log.Error("Execute OrderBasket_API_SendToPos - OrderBasketID = {0}: \nResult: {1}", orderBasketID,
                        result);
                    }
                    else
                    {
                        jsonPayload = result;
                        Log.Info("Execute OrderBasket_API_SendToPos - OrderBasketID = {0}: \nPayload: {1}", orderBasketID,
                        jsonPayload);
                    }
                }
            }
            return jsonPayload;
        }

        /// <summary>
        /// Check Send To POS Payload response
        /// </summary>
        /// <param name="con"></param>
        /// <param name="orderBasketID"></param>
        /// <param name="jsonResponse"></param>
        public void ExecuteOrderBasket_APIResponse_SendToPos(SqlConnection con, int orderBasketID, string jsonResponse)
        {
            // Create and configure a command object
            SqlCommand com = con.CreateCommand();
            com.CommandType = CommandType.StoredProcedure;
            com.CommandText = "OrderBasket_APIResponse_SendToPos";
            com.Parameters.Add("@OrderBasketID", SqlDbType.Int).Value = orderBasketID;
            com.Parameters.Add("@json", SqlDbType.VarChar).Value = jsonResponse;
            var result = com.ExecuteScalar();
            Log.Info("Execute OrderBasket_APIResponse_SendToPos: OrderBasketID {0}: \nResult: {1}", orderBasketID, result);
        }

        /// <summary>
        /// Close Basket
        /// </summary>
        /// <param name="con"></param>
        /// <param name="orderBasketID"></param>
        /// <returns></returns>
        public string ExecuteOrderBasketClose(SqlConnection con, int orderBasketID)
        {
            string payload = "-9";
            // Create and configure a command object
            SqlCommand com = con.CreateCommand();
            com.CommandType = CommandType.StoredProcedure;
            com.CommandText = "OrderBasketClose";
            com.Parameters.Add("@OrderBasketID", SqlDbType.Int).Value = orderBasketID;
            using (IDataReader reader = com.ExecuteReader())
            {
                while (reader.Read())
                {
                    var result = (string)reader[0];
                    payload = result;
                }
            }
           Log.Info("Execute OrderBasketClose - OrderBasketID = {0}: \nPayLoad{1}", orderBasketID, payload);
            return payload;
        }

        //public IRestResponse MarkAsPaidAPI( string orderId, string payLoad)
        //{
        //    var client = new RestClient("https://api.flypaythis.com/ordering/v3/order/" + orderId + "/mark-as-paid");
        //    var request = new RestRequest(Method.POST);
  
        //    request.AddHeader("Content-Type", "text/plain");
        //    request.AddHeader("X-Flypay-API-Key", "u7f2r48x6bzwyy09vwsii");
        //    request.AddParameter("text/plain", payLoad, ParameterType.RequestBody);
        //    IRestResponse response = client.Execute(request);

        //    return response;
        //}

        //public IRestResponse SendToPOSAPI(string payLoad)
        //{
           

        //    var client = new RestClient("https://flyt-acrelec-integration.flyt-platform.com/sendToPos");
        //    var request = new RestRequest(Method.POST);
        //    request.AddHeader("Content-Type", "application/json");
        //    request.AddHeader("X-Flyt-API-Key", "hdgskIZRgBmyArKCtzkjkZIvaBjMkXVbWGvbq");
        //    request.AddParameter("application/json", payLoad, ParameterType.RequestBody);
        //    IRestResponse response = client.Execute(request);

        //    return response;
        //}

        /// <summary>
        /// Do the API Post
        /// </summary>
        /// <param name="url"></param>
        /// <param name="keyType"></param>
        /// <param name="key"></param>
        /// <param name="contentType"></param>
        /// <param name="payLoad"></param>
        /// <returns></returns>
        public IRestResponse ApiPost(string url, string keyType, string key, string contentType, string payLoad)
        {

            var client = new RestClient(url);
            var request = new RestRequest(Method.POST);
            request.AddHeader("Content-Type", contentType);
            request.AddHeader(keyType, key);
            request.AddParameter(contentType, payLoad, ParameterType.RequestBody);

            IRestResponse response = client.Execute(request);
            return response;

        }


    }
}
