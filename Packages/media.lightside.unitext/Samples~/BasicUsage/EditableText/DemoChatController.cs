using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace LightSide.Samples
{
    /// <summary>
    /// Connects the Editable Text chat composer to Gemini and renders the conversation through a message prefab.
    /// The API key is sent directly by the WebGL client and is therefore public in a deployed build.
    /// </summary>
    public sealed class DemoChatController : MonoBehaviour
    {
        private const string ApiRoot = "https://generativelanguage.googleapis.com/v1beta/models/";
        
        [SerializeField] private string apiKey1;
        [SerializeField] private string apiKey2;
        
        [SerializeField, Tooltip("Gemini model used by this demo.")]
        private string model = "gemini-3.5-flash-lite";
        [SerializeField, Min(1)] private int maxHistoryTurns = 6;
        [SerializeField, Min(1)] private int maxOutputTokens = 256;
        [SerializeField, TextArea] private string systemInstruction =
            "Reply in the user's language. Keep the answer concise and use plain text.";
        [SerializeField] private UniTextEditable input;
        [SerializeField] private Button sendButton;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform content;
        [SerializeField] private DemoChatMessage leftMessagePrefab;
        [SerializeField] private DemoChatMessage rightMessagePrefab;

        private readonly List<Content> history = new();
        private UnityWebRequest activeRequest;
        private bool isRequesting;
        private bool scrollPending;

        private void Awake()
        {
            if (input == null || sendButton == null || scrollRect == null || content == null || leftMessagePrefab == null || rightMessagePrefab == null)
                throw new InvalidOperationException("Demo chat references are not fully configured.");
        }

        private void OnEnable()
        {
            input.Submitted += OnSubmitted;
            sendButton.onClick.AddListener(SendCurrentText);
        }

        private void OnDisable()
        {
            input.Submitted -= OnSubmitted;
            sendButton.onClick.RemoveListener(SendCurrentText);
            activeRequest?.Abort();
        }

        private void OnSubmitted(string _) => SendCurrentText();

        private void SendCurrentText()
        {
            if (isRequesting) return;

            var plainText = input.VisibleText.Trim();
            if (plainText.Length == 0) return;

            if (string.IsNullOrWhiteSpace(apiKey1 + apiKey2))
            {
                AddMessage("Add a Gemini API key to DemoChatController in the Inspector.", true);
                return;
            }

            AddMessage(plainText, false);
            input.SetText(string.Empty);
            EventSystem.current?.SetSelectedGameObject(input.gameObject);
            input.Activate();
            history.Add(CreateContent("user", plainText));

            var pending = AddMessage("...", true);
            StartCoroutine(RequestResponse(pending));
        }

        private IEnumerator RequestResponse(DemoChatMessage pending)
        {
            SetRequesting(true);
            var payload = new GenerateRequest
            {
                contents = history.ToArray(),
                systemInstruction = new Instruction
                {
                    parts = new[] { new Part { text = systemInstruction ?? string.Empty } },
                },
                generationConfig = new GenerationConfig
                {
                    maxOutputTokens = maxOutputTokens,
                },
            };
            var body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            var url = ApiRoot + model + ":generateContent";

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("x-goog-api-key", (apiKey1 + apiKey2).Trim());
                activeRequest = request;

                yield return request.SendWebRequest();
                activeRequest = null;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    FailRequest(pending, $"Request failed: {request.error} (HTTP {request.responseCode}).");
                    yield break;
                }

                GenerateResponsePayload response;
                try
                {
                    response = JsonUtility.FromJson<GenerateResponsePayload>(request.downloadHandler.text);
                }
                catch (ArgumentException exception)
                {
                    Debug.LogException(exception, this);
                    FailRequest(pending, "The service returned an invalid response.");
                    yield break;
                }

                var reply = ReadReply(response);
                if (reply.Length == 0)
                {
                    FailRequest(pending, "The service returned an empty response.");
                    yield break;
                }

                pending.SetText(reply);
                history.Add(CreateContent("model", reply));
                TrimHistory();
                ScheduleScrollToBottom();
                SetRequesting(false);
            }
        }

        private DemoChatMessage AddMessage(string value, bool left)
        {
            var prefab = left ? leftMessagePrefab : rightMessagePrefab; 
            var message = Instantiate(prefab, content, false);
            message.SetText(value);
            ScheduleScrollToBottom();
            return message;
        }

        private void FailRequest(DemoChatMessage pending, string message)
        {
            history.RemoveAt(history.Count - 1);
            pending.SetText(message);
            Debug.LogError(message, this);
            ScheduleScrollToBottom();
            SetRequesting(false);
        }

        private void SetRequesting(bool value)
        {
            isRequesting = value;
            sendButton.interactable = !value;
        }

        private void TrimHistory()
        {
            var limit = Mathf.Max(1, maxHistoryTurns) * 2;
            while (history.Count > limit)
                history.RemoveRange(0, 2);
        }

        private void ScheduleScrollToBottom()
        {
            if (scrollPending) return;
            scrollPending = true;
            StartCoroutine(ScrollToBottomAfterLayout());
        }

        private IEnumerator ScrollToBottomAfterLayout()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0;
            scrollPending = false;
        }

        private static Content CreateContent(string role, string value) => new()
        {
            role = role,
            parts = new[] { new Part { text = value ?? string.Empty } },
        };

        private static string ReadReply(GenerateResponsePayload response)
        {
            if (response?.candidates == null || response.candidates.Length == 0) return string.Empty;
            var parts = response.candidates[0].content?.parts;
            if (parts == null || parts.Length == 0) return string.Empty;

            var result = new StringBuilder();
            foreach (var part in parts)
            {
                if (part?.text != null) result.Append(part.text);
            }
            return result.ToString().Trim();
        }

        [Serializable]
        private sealed class GenerateRequest
        {
            public Content[] contents;
            public Instruction systemInstruction;
            public GenerationConfig generationConfig;
        }

        [Serializable]
        private sealed class GenerateResponsePayload
        {
            public Candidate[] candidates;
        }

        [Serializable]
        private sealed class Candidate
        {
            public Content content;
        }

        [Serializable]
        private sealed class Content
        {
            public string role;
            public Part[] parts;
        }

        [Serializable]
        private sealed class Instruction
        {
            public Part[] parts;
        }

        [Serializable]
        private sealed class Part
        {
            public string text;
        }

        [Serializable]
        private sealed class GenerationConfig
        {
            public int maxOutputTokens;
        }
    }
}
