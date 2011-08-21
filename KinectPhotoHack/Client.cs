using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using PhotobucketAPI.OAuth;

namespace PhotobucketAPI
{
    /// <summary>
    /// Photobucket API Client
    /// </summary>
    public class Client
    {
        #region Delegates

        /// <summary>
        /// Delegate type for Client Responses
        /// </summary>
        /// <param name="response">object containing the response data</param>
        public delegate void ResponseCallback(ResponseArgs response);

        #endregion

        #region RequestMethods enum

        /// <summary>
        /// Supported OAuth/HTTP Request Methods
        /// </summary>
        public enum RequestMethods
        {
            GET,
            POST,
            PUT,
            DELETE
        }

        #endregion

        #region UploadType enum

        /// <summary>
        /// represents a media upload type
        /// </summary>
        public enum UploadType
        {
            image,
            video
        }

        #endregion

        ///////////////////////////////////////////////////////////////////////

        /// <summary>base url for requests</summary>
        protected const string API_BASE_DOMAIN = "api.photobucket.com";

        /// <summary>useragent to send when possible</summary>
        protected const string CLIENT_USERAGENT = "PhotobucketAPI.Client";

        /// End API constants  It is not recommended to edit below here.
        /// <summary>
        /// Default GET Timeout
        /// </summary>
        protected static readonly TimeSpan GetRequestTimeout = new TimeSpan(0, 1, 0); //1 minute

        /// <summary>
        /// Default POST Timeout (includes uploads)
        /// </summary>
        protected static readonly TimeSpan PostRequestTimeout = new TimeSpan(0, 5, 0); //5 minutes

        /// <summary>
        /// Web Client for the timestamp check
        /// </summary>
        private static WebClient timestampClient;

        /// <summary>
        /// AutoResetEvent signal for timestamp check
        /// </summary>
        private static AutoResetEvent timestampSignal;

        /// <summary>
        /// shared lock for timestamp check
        /// </summary>
        private static readonly Object timestampLock = new Object();

        /// <summary>
        /// 'base' subdomain for if one is not specified in the request
        /// </summary>
        public string Subdomain;

        /// <summary>
        /// user's oauth token secret
        /// </summary>
        public string oauth_secret;

        /// <summary>
        /// user's oauth token
        /// </summary>
        public string oauth_token;

        /// <summary>
        /// Check the current network status and timestamp offset
        /// </summary>
        /// <remarks>
        /// Grabs the current timestamp (once) from the photobucket servers to construct future oauth timestamps
        /// </remarks>
        private static void checkNetworkStatus()
        {
            bool isNet = false;
            try
            {
                //This is not typically enough, however.
                isNet = NetworkInterface.GetIsNetworkAvailable();
            }
            catch
            {
                isNet = false;
            }
            if (!isNet)
            {
                throw new Exception("There is no network available");
            }

            lock (timestampLock)
            {
                if (Utilities.ServerTimestampOffset == Int32.MinValue)
                {
                    try
                    {
                        timestampSignal = new AutoResetEvent(false);
                        timestampClient = new WebClient
                                              {
                                                  //AllowReadStreamBuffering = true,
                                                  //AllowWriteStreamBuffering = true,
                                              };
                        timestampClient.DownloadStringCompleted += timestampClient_DownloadStringCompleted;
                        timestampClient.DownloadStringAsync(new Uri("http://" + API_BASE_DOMAIN + "/time"));

                        if (timestampSignal.WaitOne(new TimeSpan(0, 0, 15)))
                        {
                            // wait 15 sec
                            //signaled, we completed successfully
                            timestampClient = null;
                        }
                        else
                        {
                            try
                            {
                                timestampClient.CancelAsync();
                                timestampClient = null;
                            }
                            catch (Exception ex2)
                            {
                                Debug.WriteLine("Timestamp cancel Inner:" + ex2 + " : " + ex2.Message);
                            }
                            throw new Exception("You may have a poor network connection");
                        }
                    }
                    catch (Exception e)
                    {
                        throw e;
                    }
                    catch (System.Exception e)
                    {
                        throw new Exception(e, "You may have a poor network connection", 0);
                    }
                } //timestamp OK after this point
            } //end lock

            // todo check network ping?  etc?
        }

        /// <summary>
        /// checkNetworkStatus response callback
        /// </summary>
        /// <param name="sender">WebClient who triggered this</param>
        /// <param name="args">response arguments</param>
        private static void timestampClient_DownloadStringCompleted(object sender, DownloadStringCompletedEventArgs args)
        {
            if (timestampClient == null)
            {
                return;
            }
            timestampClient.DownloadStringCompleted -= timestampClient_DownloadStringCompleted;
            if (args == null || args.Cancelled)
            {
                return;
            }
            if (string.IsNullOrEmpty(args.Result))
            {
                return;
            }
            if (args.Error != null)
            {
                try
                {
                    timestampClient.CancelAsync();
                    timestampClient = null;
                }
                catch (Exception ex2)
                {
                    Debug.WriteLine("Inner timestamp callback:" + ex2 + " : " + ex2.Message);
                }
            }
            else
            {
                // ToInt32 can throw FormatException or OverflowException.  Ignore these as 'no action'
                int serverTime = 0;
                try
                {
                    serverTime = Convert.ToInt32(args.Result.Trim());
                    Utilities.ServerTimestampOffset = serverTime - Utilities.makeTimestamp(false);
                }
                catch
                {
                    //we'll just not do anything here right now, and not modify the offset. 
                    //we might be wrong, but at this point other stuff will be wrong anyway
                    Utilities.ServerTimestampOffset = 0;
                }

                timestampSignal.Set();
                timestampClient = null;
            }
        }

        /// <summary>
        /// Sign a request with oauth
        /// </summary>
        /// <param name="reqMethod">which http method is being used</param>
        /// <param name="url">full url (^http://) or absolute path (^/), will be automatically filled in if possible</param>
        /// <param name="args">key/value pairs of arguments which will be added to with oauth params</param>
        /// <returns>full url to request</returns>
        protected string OAuthSign(RequestMethods reqMethod, string url, ref Dictionary<string, string> args)
        {
            if (args == null)
            {
                args = new Dictionary<string, string>();
            }

            //add pb specific params
            args.Add("skiphttpcode", "1");

            //normalize uri with hostname
            Uri uri;
            if (!url.StartsWith("http://") || url.StartsWith("/"))
            {
                string host = "http://";
                if (!String.IsNullOrEmpty(Subdomain))
                {
                    host += Subdomain;
                }
                else
                {
                    host += API_BASE_DOMAIN;
                }
                uri = new Uri(host + url);
            }
            else
            {
                uri = new Uri(url);
            }

            //normalize uri for oauth
            string normalizedUrl = string.Format("{0}://{1}", uri.Scheme, "api.photobucket.com");
            if (!((uri.Scheme == "http" && uri.Port == 80) || (uri.Scheme == "https" && uri.Port == 443)))
            {
                normalizedUrl += ":" + uri.Port;
            }
            normalizedUrl += uri.AbsolutePath.TrimEnd('/');

            //add oauth params
            args.Add("oauth_version", "1.0");
            args.Add("oauth_nonce", Utilities.makeNonce());
            args.Add("oauth_timestamp", Utilities.makeTimestamp().ToString());
            args.Add("oauth_signature_method", "HMAC-SHA1");
            args.Add("oauth_consumer_key", ConfigurationManager.AppSettings["oauth_key"]);
            if (!String.IsNullOrEmpty(oauth_token))
            {
                args.Add("oauth_token", oauth_token);
            }

            //convert to List<KVP> so we can sort
            var sortArgsList = new List<KeyValuePair<string, string>>();
            foreach (var kvp in args)
            {
                sortArgsList.Add(kvp);
            }
            //sort
            sortArgsList.Sort(new KeyValuePairComparer());

            //make basestring
            string baseString = string.Format("{0}&{1}&{2}",
                                              reqMethod.ToString(),
                                              Utilities.UrlEncode3986(normalizedUrl),
                                              Utilities.UrlEncode3986(Utilities.ToQueryString(sortArgsList)));

            //make signature keystring
            string sigKey = String.Format("{0}&{1}",
                                          Utilities.UrlEncode3986(ConfigurationManager.AppSettings["oauth_secret"]),
                                          String.IsNullOrEmpty(oauth_secret)
                                              ? ""
                                              : Utilities.UrlEncode3986(oauth_secret));

            //compute signature
            var hash = new HMACSHA1
                           {
                               Key = Encoding.UTF8.GetBytes(sigKey)
                           };
            string signature = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(baseString)));

            //add to args
            args.Add("oauth_signature", signature);

            //return normalized original url string
            return string.Format("{0}://{1}{2}", uri.Scheme, uri.Host, uri.AbsolutePath);
        }

        /// <summary>
        /// Make a querystring request (Get, Put, Delete)
        /// </summary>
        /// <param name="requestMethod">one of GET, PUT, DELETE</param>
        /// <param name="url">url to request</param>
        /// <param name="args">key value pairs of arguments to put on the querystring</param>
        /// <param name="cb">callback delegate</param>
        /// <param name="userState">state object returned in the callback</param>
        public void makeQueryStringRequest(RequestMethods requestMethod,
                                           string url,
                                           Dictionary<string, string> args,
                                           ResponseCallback cb,
                                           object userState = null)
        {
            //fire up this request in its own worker thread
            ThreadPool.QueueUserWorkItem(delegate
                                             {
                                                 var response = new ResponseArgs
                                                                    {
                                                                        Context = userState
                                                                    };

                                                 try
                                                 {
                                                     //check network status (is synchronous)
                                                     checkNetworkStatus();

                                                     var timerSignal = new AutoResetEvent(false);

                                                     //construct oauth - returned string is the cleaned up domain/path, args has oauth params
                                                     string targetUriString = OAuthSign(requestMethod, url, ref args);

                                                     var req =
                                                         (HttpWebRequest)
                                                         WebRequest.Create(targetUriString + "?" +
                                                                           Utilities.ToQueryString(args));
                                                     //req.AllowReadStreamBuffering = false;
                                                     req.Method = requestMethod.ToString();
                                                     req.UserAgent = CLIENT_USERAGENT;
                                                     req.AllowAutoRedirect = (requestMethod != RequestMethods.POST);

                                                     //once the request is finished, get the response
                                                     var readArgs = new HttpResponseArgs
                                                                        {
                                                                            Request = req,
                                                                            Response = response,
                                                                            Callback = cb,
                                                                            Signal = timerSignal,
                                                                        };
                                                     req.BeginGetResponse(readResponse, readArgs);

                                                     //TODO fire 'request started' event

                                                     //request timeout
                                                     if (timerSignal.WaitOne(GetRequestTimeout))
                                                     {
                                                         //signaled, we completed
                                                         //todo fire 'complete' event
                                                     }
                                                     else
                                                     {
                                                         try
                                                         {
                                                             req.Abort();
                                                         }
                                                         catch (Exception ex2)
                                                         {
                                                             Debug.WriteLine("Inner query request exception:" + ex2 +
                                                                             " : " + ex2.Message);
                                                         }

                                                         throw new Exception(
                                                             "The server request timed out. You may have a poor network connection.");
                                                     }
                                                 }
                                                 catch (Exception ce)
                                                 {
                                                     Debug.WriteLine(ce + " : " + ce.DisplayMessage);
                                                     response.Error = ce.DisplayMessage;
                                                     response.ErrorException = ce;

                                                     cb(response);
                                                 }
                                                 catch (WebException we)
                                                 {
                                                     Debug.WriteLine("WebException: " + we.Message);
                                                     Debug.WriteLine("WebException: " + we);
                                                     response.ErrorException = we;
                                                     response.Error = we.Message;

                                                     cb(response);
                                                 }
                                                 catch (System.Exception e)
                                                 {
                                                     Debug.WriteLine(e.ToString());
                                                     response.Error = "Exception: " + e.Message;
                                                     response.ErrorException = e;

                                                     cb(response);
                                                 }

                                                 //fire request finished event (with response)

                                                 //end of thread
                                             });
        }

        /// <summary>
        /// Make a www-urlencoded post body request (always method=POST)
        /// </summary>
        /// <param name="url">url to post to</param>
        /// <param name="args">key value arguments</param>
        /// <param name="cb">callback</param>
        /// <param name="userState">state object returned in callback</param>
        public void makePost(string url,
                             Dictionary<string, string> args,
                             ResponseCallback cb,
                             object userState = null)
        {
            //create a worker thread for this request
            ThreadPool.QueueUserWorkItem(delegate
                                             {
                                                 var response = new ResponseArgs
                                                                    {
                                                                        Context = userState
                                                                    };

                                                 try
                                                 {
                                                     //synchronous network status check
                                                     checkNetworkStatus();

                                                     var timerSignal = new AutoResetEvent(false);

                                                     //generate uri and signed parameters
                                                     string targetUriString = OAuthSign(RequestMethods.POST, url,
                                                                                        ref args);

                                                     //setup the httpwebrequest 
                                                     var req = (HttpWebRequest) WebRequest.Create(targetUriString);
                                                     //req.AllowReadStreamBuffering = false;
                                                     req.ContentType = "application/x-www-form-urlencoded";
                                                     req.Method = "POST";
                                                     req.AllowAutoRedirect = false;
                                                     req.UserAgent = CLIENT_USERAGENT;

                                                     //start the request here.
                                                     req.BeginGetRequestStream(delegate(IAsyncResult requestResult)
                                                                                   {
                                                                                       try
                                                                                       {
                                                                                           //once the request is set up, put the output on the stream
                                                                                           var asyncRequest =
                                                                                               (HttpWebRequest)
                                                                                               requestResult.AsyncState;
                                                                                           Stream postStream =
                                                                                               asyncRequest.
                                                                                                   EndGetRequestStream(
                                                                                                       requestResult);

                                                                                           byte[] outBytes =
                                                                                               Encoding.UTF8.GetBytes(
                                                                                                   Utilities.
                                                                                                       ToQueryString(
                                                                                                           args));
                                                                                           int len = outBytes.Length;
                                                                                           postStream.Write(outBytes, 0,
                                                                                                            outBytes.
                                                                                                                Length);
                                                                                           postStream.Flush();
                                                                                           postStream.Close();

                                                                                           //fire request completed event

                                                                                           //once the request is finished, get the response
                                                                                           var readArgs = new HttpResponseArgs
                                                                                                              {
                                                                                                                  Request
                                                                                                                      =
                                                                                                                      req,
                                                                                                                  Response
                                                                                                                      =
                                                                                                                      response,
                                                                                                                  Callback
                                                                                                                      =
                                                                                                                      cb,
                                                                                                                  Signal
                                                                                                                      =
                                                                                                                      timerSignal,
                                                                                                              };
                                                                                           req.BeginGetResponse(
                                                                                               readResponse, readArgs);
                                                                                       }
                                                                                       catch (Exception ce)
                                                                                       {
                                                                                           try
                                                                                           {
                                                                                               timerSignal.Set();
                                                                                               req.Abort();
                                                                                           }
                                                                                           catch (Exception ex2)
                                                                                           {
                                                                                               Debug.WriteLine(
                                                                                                   "Inner post request exception:" +
                                                                                                   ex2 + " : " +
                                                                                                   ex2.Message);
                                                                                           }

                                                                                           Debug.WriteLine(ce + " : " +
                                                                                                           ce.
                                                                                                               DisplayMessage);
                                                                                           response.Error =
                                                                                               ce.DisplayMessage;
                                                                                           response.ErrorException = ce;

                                                                                           cb(response);
                                                                                       }
                                                                                       catch (WebException we)
                                                                                       {
                                                                                           try
                                                                                           {
                                                                                               timerSignal.Set();
                                                                                               req.Abort();
                                                                                           }
                                                                                           catch (Exception ex2)
                                                                                           {
                                                                                               Debug.WriteLine(
                                                                                                   "Inner post request exception:" +
                                                                                                   ex2 + " : " +
                                                                                                   ex2.Message);
                                                                                           }

                                                                                           Debug.WriteLine(
                                                                                               "WebException: " +
                                                                                               we.Message);
                                                                                           Debug.WriteLine(
                                                                                               "WebException: " + we);
                                                                                           response.ErrorException = we;
                                                                                           response.Error = we.Message;

                                                                                           cb(response);
                                                                                       }
                                                                                       catch (System.Exception e)
                                                                                       {
                                                                                           try
                                                                                           {
                                                                                               timerSignal.Set();
                                                                                               req.Abort();
                                                                                           }
                                                                                           catch (Exception ex2)
                                                                                           {
                                                                                               Debug.WriteLine(
                                                                                                   "Inner post request exception:" +
                                                                                                   ex2 + " : " +
                                                                                                   ex2.Message);
                                                                                           }

                                                                                           Debug.WriteLine(e.ToString());
                                                                                           response.Error =
                                                                                               "Exception: " + e.Message;
                                                                                           response.ErrorException = e;

                                                                                           cb(response);
                                                                                       }

                                                                                       //end of request handling
                                                                                   }, req);

                                                     //fire request started event

                                                     //timeout
                                                     if (timerSignal.WaitOne(PostRequestTimeout))
                                                     {
                                                         //signaled, we completed
                                                     }
                                                     else
                                                     {
                                                         try
                                                         {
                                                             req.Abort();
                                                         }
                                                         catch (Exception ex2)
                                                         {
                                                             Debug.WriteLine("Inner:" + ex2 + " : " + ex2.Message);
                                                         }

                                                         throw new Exception(
                                                             "The request timed out. You may have a poor network connection.");
                                                     }
                                                 }
                                                 catch (Exception ce)
                                                 {
                                                     Debug.WriteLine(ce + " : " + ce.DisplayMessage);
                                                     response.Error = ce.DisplayMessage;
                                                     response.ErrorException = ce;

                                                     cb(response);
                                                 }
                                                 catch (WebException we)
                                                 {
                                                     Debug.WriteLine("WebException: " + we.Message);
                                                     Debug.WriteLine("WebException: " + we);
                                                     response.ErrorException = we;
                                                     response.Error = we.Message;

                                                     cb(response);
                                                 }
                                                 catch (System.Exception e)
                                                 {
                                                     Debug.WriteLine(e.ToString());
                                                     response.Error = "Exception: " + e.Message;
                                                     response.ErrorException = e;

                                                     cb(response);
                                                 }

                                                 // fire response complete event

                                                 //end of thread
                                             });
        }

        /// <summary>
        /// Upload a media
        /// </summary>
        /// <param name="item">item object representing the upload media</param>
        /// <param name="albumId">album id (username/location)</param>
        /// <param name="cb">callback after upload is complete</param>
        /// <param name="userState">state to bring along, returned in callback object</param>
        public void uploadItem(IUpload item, string albumId, ResponseCallback cb, object userState = null)
        {
            //create a worker thread for this request
            ThreadPool.QueueUserWorkItem(delegate
                                             {
                                                 var response = new ResponseArgs
                                                                    {
                                                                        Context = userState
                                                                    };

                                                 string Url = "/album/!/upload";
                                                 var Args = new Dictionary<string, string>
                                                                {
                                                                    {"type", item.Type.ToString()},
                                                                    {"title", item.Title},
                                                                    {"description", item.Description},
                                                                    {"id", albumId},
                                                                };

                                                 try
                                                 {
                                                     //synchronous network status check
                                                     checkNetworkStatus();

                                                     var timerSignal = new AutoResetEvent(false);

                                                     //generate uri and signed parameters
                                                     string targetUriString = OAuthSign(RequestMethods.POST, Url,
                                                                                        ref Args);

                                                     string mpBoundary = "----" + DateTime.Now.Ticks.ToString("x");

                                                     //setup the http post request
                                                     var req = (HttpWebRequest) WebRequest.Create(targetUriString);
                                                     //req.AllowReadStreamBuffering = false;
                                                     req.ContentType = "multipart/form-data; boundary=" + mpBoundary;
                                                     req.Method = "POST";
                                                     req.AllowAutoRedirect = false;
                                                     req.UserAgent = CLIENT_USERAGENT;

                                                     //start the request here.
                                                     req.BeginGetRequestStream(delegate(IAsyncResult requestResult)
                                                                                   {
                                                                                       try
                                                                                       {
                                                                                           //once the request is set up, put the output on the stream
                                                                                           var asyncRequest =
                                                                                               (HttpWebRequest)
                                                                                               requestResult.AsyncState;
                                                                                           Stream postStream =
                                                                                               asyncRequest.
                                                                                                   EndGetRequestStream(
                                                                                                       requestResult);

                                                                                           //TODO fire progress

                                                                                           //build the multipart text parameters
                                                                                           string formDataTemplate =
                                                                                               "\r\n--" + mpBoundary +
                                                                                               "\r\nContent-Disposition: form-data; name=\"{0}\";\r\n\r\n{1}";
                                                                                           byte[] outBytes;
                                                                                           foreach (var a in Args)
                                                                                           {
                                                                                               outBytes =
                                                                                                   Encoding.UTF8.
                                                                                                       GetBytes(
                                                                                                           string.Format
                                                                                                               (formDataTemplate,
                                                                                                                a.Key,
                                                                                                                a.Value));
                                                                                               postStream.Write(
                                                                                                   outBytes, 0,
                                                                                                   outBytes.Length);
                                                                                           }

                                                                                           //append file
                                                                                           string uploadDataTemplate =
                                                                                               "\r\n--" + mpBoundary
                                                                                               +
                                                                                               "\r\nContent-Disposition: form-data; name=\"{0}\"; filename=\"{1}\";"
                                                                                               +
                                                                                               "\r\nContent-Transfer-Encoding: binary"
                                                                                               +
                                                                                               "\r\nContent-Type: application/octet-stream\r\n\r\n";

                                                                                           outBytes =
                                                                                               Encoding.UTF8.GetBytes(
                                                                                                   string.Format(
                                                                                                       uploadDataTemplate,
                                                                                                       "uploadfile",
                                                                                                       item.Filename));
                                                                                           postStream.Write(outBytes, 0,
                                                                                                            outBytes.
                                                                                                                Length);

                                                                                           //todo send another event noting we're starting file xfer?
                                                                                           int bytesRead = 0;
                                                                                           long sentBytes = 0;
                                                                                           var buffer = new byte[4096];
                                                                                           while (
                                                                                               (bytesRead =
                                                                                                item.Stream.Read(
                                                                                                    buffer, 0,
                                                                                                    buffer.Length)) != 0)
                                                                                           {
                                                                                               postStream.Write(buffer,
                                                                                                                0,
                                                                                                                bytesRead);
                                                                                               sentBytes += bytesRead;
                                                                                               //todo send an event (only every 1%?)
                                                                                           }
                                                                                           outBytes =
                                                                                               Encoding.UTF8.GetBytes(
                                                                                                   "\r\n--" + mpBoundary);
                                                                                           postStream.Write(outBytes, 0,
                                                                                                            outBytes.
                                                                                                                Length);

                                                                                           postStream.Flush();
                                                                                           postStream.Dispose();
                                                                                           postStream = null;

                                                                                           //once the request is finished, get the response
                                                                                           var readArgs = new HttpResponseArgs
                                                                                                              {
                                                                                                                  Request
                                                                                                                      =
                                                                                                                      req,
                                                                                                                  Response
                                                                                                                      =
                                                                                                                      response,
                                                                                                                  Callback
                                                                                                                      =
                                                                                                                      cb,
                                                                                                                  Signal
                                                                                                                      =
                                                                                                                      timerSignal,
                                                                                                              };
                                                                                           req.BeginGetResponse(
                                                                                               readResponse, readArgs);
                                                                                       }
                                                                                       catch (Exception ce)
                                                                                       {
                                                                                           try
                                                                                           {
                                                                                               timerSignal.Set();
                                                                                               req.Abort();
                                                                                           }
                                                                                           catch (Exception ex2)
                                                                                           {
                                                                                               Debug.WriteLine(
                                                                                                   "Inner upload request exception:" +
                                                                                                   ex2 + " : " +
                                                                                                   ex2.Message);
                                                                                           }

                                                                                           Debug.WriteLine(ce + " : " +
                                                                                                           ce.
                                                                                                               DisplayMessage);
                                                                                           response.Error =
                                                                                               ce.DisplayMessage;
                                                                                           response.ErrorException = ce;

                                                                                           cb(response);
                                                                                       }
                                                                                       catch (WebException we)
                                                                                       {
                                                                                           try
                                                                                           {
                                                                                               timerSignal.Set();
                                                                                               req.Abort();
                                                                                           }
                                                                                           catch (Exception ex2)
                                                                                           {
                                                                                               Debug.WriteLine(
                                                                                                   "Inner upload request exception:" +
                                                                                                   ex2 + " : " +
                                                                                                   ex2.Message);
                                                                                           }

                                                                                           Debug.WriteLine(
                                                                                               "WebException: " +
                                                                                               we.Message);
                                                                                           Debug.WriteLine(
                                                                                               "WebException: " + we);
                                                                                           response.ErrorException = we;
                                                                                           response.Error = we.Message;

                                                                                           cb(response);
                                                                                       }
                                                                                       catch (System.Exception e)
                                                                                       {
                                                                                           try
                                                                                           {
                                                                                               timerSignal.Set();
                                                                                               req.Abort();
                                                                                           }
                                                                                           catch (Exception ex2)
                                                                                           {
                                                                                               Debug.WriteLine(
                                                                                                   "Inner upload request exception:" +
                                                                                                   ex2 + " : " +
                                                                                                   ex2.Message);
                                                                                           }

                                                                                           Debug.WriteLine(e.ToString());
                                                                                           response.Error =
                                                                                               "Exception: " + e.Message;
                                                                                           response.ErrorException = e;

                                                                                           cb(response);
                                                                                       }

                                                                                       //end of request handling
                                                                                   }, req);

                                                     //timeout
                                                     if (timerSignal.WaitOne(PostRequestTimeout))
                                                     {
                                                         //signaled, we completed
                                                     }
                                                     else
                                                     {
                                                         try
                                                         {
                                                             req.Abort();
                                                         }
                                                         catch (Exception ex2)
                                                         {
                                                             Debug.WriteLine("Inner:" + ex2 + " : " + ex2.Message);
                                                         }

                                                         throw new Exception(
                                                             "The request timed out. You may have a poor network connection.");
                                                     }
                                                 }
                                                 catch (Exception ce)
                                                 {
                                                     Debug.WriteLine(ce + " : " + ce.DisplayMessage);
                                                     response.Error = ce.DisplayMessage;
                                                     response.ErrorException = ce;

                                                     cb(response);
                                                 }
                                                 catch (WebException we)
                                                 {
                                                     Debug.WriteLine("WebException: " + we.Message);
                                                     Debug.WriteLine("WebException: " + we);
                                                     response.ErrorException = we;
                                                     response.Error = we.Message;

                                                     cb(response);
                                                 }
                                                 catch (System.Exception e)
                                                 {
                                                     Debug.WriteLine(e.ToString());
                                                     response.Error = "Exception: " + e.Message;
                                                     response.ErrorException = e;

                                                     cb(response);
                                                 }

                                                 //end of thread
                                             });
        }

        /// <summary>
        /// Read the HTTP Response
        /// </summary>
        /// <param name="responseResult">object from begingetresponse</param>
        protected void readResponse(IAsyncResult responseResult)
        {
            var t = (HttpResponseArgs) responseResult.AsyncState;
            ResponseArgs response = t.Response;

            try
            {
                WebResponse res = t.Request.EndGetResponse(responseResult);

                //todo look at status code + description
                if (res.ContentLength == 0)
                {
                    throw new Exception("The response was empty");
                }
                if (res.ContentType != "text/xml")
                {
                    throw new Exception("The response was wrong");
                }

                Stream getStream = res.GetResponseStream();
                response.ResponseXML = parseXML(getStream);
                getStream.Dispose();
                getStream = null;
                res = null;

                //end successful
            }
            catch (Exception ce)
            {
                try
                {
                    t.Request.Abort();
                }
                catch (Exception ex2)
                {
                    Debug.WriteLine("Inner:" + ex2 + " : " + ex2.Message);
                }

                Debug.WriteLine(ce + " : " + ce.DisplayMessage);
                response.Error = ce.DisplayMessage;
                response.ErrorException = ce;
            }
            catch (System.Exception ex)
            {
                try
                {
                    t.Request.Abort();
                }
                catch (Exception ex2)
                {
                    Debug.WriteLine("Inner:" + ex2 + " : " + ex2.Message);
                }

                Debug.WriteLine("Exception: " + ex.Message);
                Debug.WriteLine("Exception: " + ex);
                response.ErrorException = ex;
                response.Error = ex.Message;
            }
            finally
            {
                t.Request = null;
                t.Signal.Set();
                t.Callback(response);
            }
        }

        /// <summary>
        /// Parse xml response from api
        /// </summary>
        /// <param name="stream">response stream (already set to 0)</param>
        /// <returns>content element from response, throwing away the envelope</returns>
        protected static XElement parseXML(Stream stream)
        {
            XDocument resultDoc = XDocument.Load(stream);

            if (resultDoc == null)
            {
                throw new Exception("The response couldn't be read");
            }

            if (resultDoc.Element("response") == null)
            {
                throw new Exception("The response couldn't be read");
            }
            XElement result = resultDoc.Element("response");

            //check for error
            if (result.Element("status") == null)
            {
                throw new Exception("The response couldn't be read");
            }
            if (result.Element("status").Value != "OK")
            {
                //we're in an error state
                if (result.Element("message") != null)
                {
                    int code = 0;
                    if (result.Element("code") != null)
                    {
                        try
                        {
                            code = Convert.ToInt32(result.Element("code").Value.Trim());
                        }
                        catch
                        {
                            //some error occured converting the code.  dont bother doing anything else
                        }
                    }
                    throw new Exception(result.Element("message").Value, code);
                }
                throw new Exception("The response couldn't be read");
            }

            if (result.Element("content") == null)
            {
                throw new Exception("The response contained no content");
            }

            return result.Element("content");
        }

        #region Nested type: Exception

        /// <summary>
        /// Client Exception
        /// </summary>
        public class Exception : System.Exception
        {
            //TODO possibly extend a different exception that is more suited.

            /// <summary>
            /// Generic API Exception
            /// </summary>
            /// <param name="message">message to give (to the user probably)</param>
            /// <param name="code"></param>
            public Exception(string message = "", int code = 0)
                : base(message)
            {
                Code = code;
            }

            /// <summary>
            /// Chained API Exception
            /// </summary>
            /// <param name="innerException"></param>
            /// <param name="message"></param>
            /// <param name="code"></param>
            public Exception(System.Exception innerException, string message = "", int code = 0)
                : base(message, innerException)
            {
                Code = code;
            }

            /// <summary>
            /// Exception integer code
            /// </summary>
            public int Code { get; set; }

            /// <summary>
            /// Exception message
            /// </summary>
            public string UserMessage { get; set; }

            /// <summary>
            /// Exception to display to the user
            /// </summary>
            public string DisplayMessage
            {
                get
                {
                    if (!string.IsNullOrEmpty(UserMessage))
                    {
                        return UserMessage;
                    }
                    else if (!string.IsNullOrEmpty(Message))
                    {
                        return Message;
                    }
                    else
                    {
                        return "Network error";
                    }
                }
            }
        }

        #endregion

        #region Nested type: HttpResponseArgs

        protected class HttpResponseArgs
        {
            public ResponseCallback Callback;
            public HttpWebRequest Request;
            public ResponseArgs Response;
            public AutoResetEvent Signal;
        };

        #endregion

        #region Nested type: IUpload

        /// <summary>
        /// Upload container object
        /// </summary>
        public interface IUpload
        {
            /// <summary>
            /// Filename to represent this  media
            /// </summary>
            string Filename { get; set; }

            /// <summary>
            /// Title for this media (will be set on upload)
            /// </summary>
            string Title { get; set; }

            /// <summary>
            /// Description for this media (will be set on upload)
            /// </summary>
            string Description { get; set; }

            /// <summary>
            /// Type of media to be uploaded (must represent the media properly)
            /// </summary>
            UploadType Type { get; set; }

            /// <summary>
            /// handle to the file data
            /// </summary>
            Stream Stream { get; set; }
        }

        #endregion

        #region Nested type: ResponseArgs

        /// <summary>
        /// Callback arguments
        /// </summary>
        public class ResponseArgs
        {
            public object Context;
            public string Error;
            public System.Exception ErrorException;
            public XElement ResponseXML;
        }

        #endregion
    }
}