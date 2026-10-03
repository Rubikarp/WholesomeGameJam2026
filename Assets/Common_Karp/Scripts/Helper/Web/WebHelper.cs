using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Wrapper async minimal autour de UnityWebRequest (compatible WebGL — pas de Thread).
/// ⚠️ Ce fichier est fourni parce qu'aucun WebHelper n'existait dans ce projet.
/// Si votre lib partagée en contient déjà un avec la même signature, supprimez celui-ci.
/// </summary>
public static class WebHelper
{
	public static async Awaitable<string> GetRequestAsync( string url, 
		(string key, string value)[] headers = null, 
		CancellationToken cancellationToken = default)
	{
		return await SendAsync(UnityWebRequest.Get(url), headers, cancellationToken);
	}

	public static async Awaitable<string> PostRequestAsync( string url, string body,
		(string key, string value)[] headers = null,
		CancellationToken cancellationToken = default)
	{
		var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
		byte[] payload = Encoding.UTF8.GetBytes(body ?? string.Empty);
		request.uploadHandler = new UploadHandlerRaw(payload);
		request.downloadHandler = new DownloadHandlerBuffer();
		return await SendAsync(request, headers, cancellationToken);
	}

	private static async Awaitable<string> SendAsync( UnityWebRequest request,
		(string key, string value)[] headers,
		CancellationToken cancellationToken)
	{
		using (request)
		{
			if (headers != null)
			{
				foreach (var (key, value) in headers)
				{
					request.SetRequestHeader(key, value);
				}
			}

			UnityWebRequestAsyncOperation operation = request.SendWebRequest();
			while (!operation.isDone)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await Awaitable.NextFrameAsync(cancellationToken);
			}

			if (request.result != UnityWebRequest.Result.Success)
			{
				throw new WebHelperException(request.responseCode, request.error, request.downloadHandler?.text);
			}

			return request.downloadHandler.text;
		}
	}
}