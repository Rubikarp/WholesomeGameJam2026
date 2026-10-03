using System.Text;
using TMPro;
using UnityEngine;
using Alchemy.Inspector;

[RequireComponent(typeof(TextMeshProUGUI))]
public class LoremIpsumTextGenerator : MonoBehaviour
{
    private static readonly string[] Words =
    {
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit",
        "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore",
        "magna", "aliqua", "ut", "enim", "ad", "minim", "veniam", "quis", "nostrud",
        "exercitation", "ullamco", "laboris", "nisi", "ut", "aliquip", "ex", "ea",
        "commodo", "consequat", "duis", "aute", "irure", "dolor", "in", "reprehenderit",
        "in", "voluptate", "velit", "esse", "cillum", "dolore", "eu", "fugiat", "nulla",
        "pariatur", "excepteur", "sint", "occaecat", "cupidatat", "non", "proident",
        "sunt", "in", "culpa", "qui", "officia", "deserunt", "mollit", "anim", "id",
        "est", "laborum"
    };

    [Header("References")]
    [SerializeField] private TextMeshProUGUI textSlot;

    [Header("Generation")]
    [SerializeField, Min(1)] private int wordCount = 24;
    [SerializeField, Min(1)] private int sentenceCount = 1;
    [SerializeField, Min(1)] private int paragraphCount = 1;
    [SerializeField] private bool generateOnStart = true;

    [Header("Style")]
    [SerializeField] private bool capitalizeFirstLetter = true;
    [SerializeField] private bool endWithPeriod = true;
    [SerializeField] private bool useDoubleNewLineBetweenParagraphs = true;

    private void Awake()
    {
        if (textSlot == null)
            textSlot = GetComponent<TextMeshProUGUI>();
    }

    private void Start()
    {
        if (generateOnStart)
            Generate();
    }

    private void OnValidate()
    {
        if (textSlot == null)
            textSlot = GetComponent<TextMeshProUGUI>();

        if (!Application.isPlaying)
            Generate();
    }

    [Button]
    public void Generate()
    {
        if (textSlot == null)
            return;

        textSlot.text = BuildText();
    }

    [Button]
    public void Clear()
    {
        if (textSlot == null)
            return;

        textSlot.text = string.Empty;
    }

    private string BuildText()
    {
        var sb = new StringBuilder();

        for (int p = 0; p < paragraphCount; p++)
        {
            for (int s = 0; s < sentenceCount; s++)
            {
                string sentence = BuildSentence(wordCount);

                if (capitalizeFirstLetter)
                    sentence = char.ToUpper(sentence[0]) + sentence.Substring(1);

                sb.Append(sentence);

                if (endWithPeriod && !sentence.EndsWith("."))
                    sb.Append('.');

                if (s < sentenceCount - 1)
                    sb.Append(' ');
            }

            if (p < paragraphCount - 1)
                sb.Append(useDoubleNewLineBetweenParagraphs ? "\n\n" : "\n");
        }

        return sb.ToString();
    }

    private string BuildSentence(int count)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                sb.Append(' ');

            sb.Append(Words[Random.Range(0, Words.Length)]);
        }

        return sb.ToString();
    }
}
