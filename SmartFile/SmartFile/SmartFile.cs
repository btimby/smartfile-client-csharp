using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections;

using SmartFile.Errors;

namespace SmartFile
{
	namespace Errors
	{
		public class APIError : Exception
		{
			public APIError(String message)
			{
			}
		}

		public class RequestError : APIError
		{
			public RequestError(String message)
				: base(message)
			{
			}
		}

		public class ResponseError: APIError
		{
			private WebException error;

			public ResponseError(WebException e)
				: base("Server responded with error")
			{
				this.error = e;
			}

			public int status_code
			{
				get {
					return (int)this.error.Status;
				}
			}

			public WebResponse response
			{
				get {
					return this.error.Response;
				}
			}
		}
	}
	internal class Util
	{
		public static bool isValidToken(String token)
		{
			if (String.IsNullOrEmpty(token)) {
				return false;
			}
			if (token.Length < 30) {
				return false;
			}
			return true;
		}
	}

	public abstract class Client
	{
		protected const String API_URL = "https://app.smartfile.com/";
		protected const String API_VER = "2.1";
		protected const String HTTP_USER_AGENT = "SmartFile C# API client v{0}";
		protected const String THROTTLE_PATTERN = "^.*; next=([\\d\\.]+) sec$";

		public String url;
		public String version;
		public bool throttleWait;
		public Regex throttleRegex;

		public Client(String url = API_URL, String version = API_VER, bool throttleWait = true)
		{
			this.url = url;
			this.version = version;
			this.throttleWait = throttleWait;
			this.throttleRegex = new Regex(THROTTLE_PATTERN);
		}

		protected WebResponse _doRequest(WebRequest request)
		{
			WebResponse response;
			try
			{
				response = request.GetResponse();
			} catch (WebException e)
			{
				throw new ResponseError(e);
			}
			// figure out the return type we want...
			return response;
		}
	
		protected WebResponse _request(String method, String endpoint, object id = null, Hashtable data = null, Hashtable query = null)
		{
			ArrayList parts = new ArrayList();
			parts.Add("api");
			parts.Add(this.version);
			parts.Add(endpoint);
			if (id != null)
				parts.Add(id.ToString());
			String path = String.Join("/", (string[])parts.ToArray());
			if (!path.EndsWith("/"))
				path += "/";
			while (path.Contains("//"))
				path = path.Replace("//", "/");
			String url = this.url + path;
			WebRequest request = HttpWebRequest.Create(url);
			request.Method = method;
			request.Headers.Add("User-Agent", String.Format(HTTP_USER_AGENT, this.version));
			int trys = 0;
			for (; trys < 3; trys++)
			{
				try
				{
					return this._doRequest(request);
				} catch (ResponseError e)
				{
					if (this.throttleWait && e.status_code == 503)
					{
						String throttleHeader = e.response.Headers.Get("x-throttle");
						if (!String.IsNullOrEmpty(throttleHeader))
						{
							float wait;
							Match m = this.throttleRegex.Match(throttleHeader);
							if (float.TryParse(m.Groups[1].Value, out wait))
							{
								Thread.Sleep((int)(wait*1000));
								continue;
							}
						}
					}
					throw e;
				}
			}
			throw new RequestError(String.Format ("Could not complete request after {0} trys.", trys));
		}

		public WebResponse get(String endpoint, object id = null, Hashtable data = null)
		{
			return this._request("GET", endpoint, id, query: data);
		}

		public WebResponse put(String endpoint, object id = null, Hashtable data = null)
		{
			return this._request("PUT", endpoint, id, data: data);
		}

		public WebResponse post(String endpoint, object id = null, Hashtable data = null)
		{
			return this._request("POST", endpoint, id, data: data);
		}

		public WebResponse delete(String endpoint, object id = null, Hashtable data = null)
		{
			return this._request("DELETE", endpoint, id, data: data);
		}
	}

	public class BasicClient : Client
	{
		private String key;
		private String password;

		public BasicClient(String key = null, String password = null, String url = Client.API_URL, String version = Client.API_VER, bool throttleWait = true)
		{
			if (!Util.isValidToken(key)) {
				key = Environment.GetEnvironmentVariable("SMARTFILE_API_KEY");
			}
			if (!Util.isValidToken(password)) {
				password = Environment.GetEnvironmentVariable("SMARTFILE_API_PASSWORD");
			}
			if (!Util.isValidToken(key) || !Util.isValidToken(password)) {
				throw new APIError("Please provide an API key and password. Use arguments or environment variables.");
			}
			this.key = key;
			this.password = password;
		}

		protected new WebResponse _doRequest(WebRequest request)
		{
			// Add key/password to web request. Explicitly add the header so that we don't
			// rely upon a challenge.
			string authInfo = this.key + ":" + this.password;
			authInfo = Convert.ToBase64String(Encoding.Default.GetBytes(authInfo));
			request.Headers["Authorization"] = "Basic " + authInfo;
			return base._doRequest(request);
		}
	}
}
