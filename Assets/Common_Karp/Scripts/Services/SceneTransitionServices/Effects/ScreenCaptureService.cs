using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine;
using System.Threading;
using System;

namespace WorldGame.Tool
{
	public static class ScreenCaptureService
	{
		public static async Awaitable<Texture2D> CaptureScreenAsync(CancellationToken cancellationToken)
		{
			// CaptureScreenshotAsTexture n'est valide qu'après le rendu complet de la frame.
			await Awaitable.EndOfFrameAsync(cancellationToken);

			return ScreenCapture.CaptureScreenshotAsTexture();
		}

		public static async Awaitable<Texture2D> CaptureCameraAsync(Camera camera, int width, int height, CancellationToken cancellationToken)
		{
			if (camera == null)
			{
				throw new ArgumentNullException(nameof(camera));
			}

			if (width <= 0 || height <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(width), "Dimensions de capture invalides.");
			}

			await Awaitable.EndOfFrameAsync(cancellationToken);

			RenderTextureDescriptor descriptor =
				new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24)
				{
					msaaSamples = 1,
					sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
				};

			RenderTexture renderTexture = RenderTexture.GetTemporary(descriptor);
			RenderTexture previousActive = RenderTexture.active;

			try
			{
				UniversalRenderPipeline.SingleCameraRequest request = new UniversalRenderPipeline.SingleCameraRequest
				{
					destination = renderTexture,
				};
				if (!RenderPipeline.SupportsRenderRequest(camera, request))
				{
					throw new NotSupportedException("SingleCameraRequest non supporté par le pipeline actif.");
				}

				RenderPipeline.SubmitRenderRequest(camera, request);
				RenderTexture.active = renderTexture;

				Texture2D capture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
				capture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
				capture.Apply(false, false);

				return capture;
			}
			finally
			{
				RenderTexture.active = previousActive;
				RenderTexture.ReleaseTemporary(renderTexture);
			}
		}
	}
}