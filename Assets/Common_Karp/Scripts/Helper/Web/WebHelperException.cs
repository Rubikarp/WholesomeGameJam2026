using System;

public class WebHelperException : Exception
{
	public long StatusCode { get; }
	public string ResponseBody { get; }

	public WebHelperException(long statusCode, string error, string responseBody) : base($"HTTP {statusCode}: {error}")
	{
		StatusCode = statusCode;
		ResponseBody = responseBody;
	}
}