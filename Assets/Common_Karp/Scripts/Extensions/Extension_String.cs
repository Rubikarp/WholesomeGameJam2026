using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

public static class Extension_String
{
	private static readonly string ALPHABET_MAJ = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
	private static readonly string ALPHABET = "abcdefghijklmnopqrstuvwxyz";
    public static string RandomString(int length) => RandomString(length, "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
	public static string RandomString(int length, string chars)
	{
		return new string(Enumerable.Repeat(chars, length).Select(s => s[UnityEngine.Random.Range(0, chars.Length)]).ToArray());
	}

    public static string ConvertMarkdownToTmpBalise(this string markdown, Color urlColor) => markdown.ConvertMarkdownToTmpBalise(ColorUtility.ToHtmlStringRGB(urlColor));
    public static string ConvertMarkdownToTmpBalise(this string markdown, string urlColorHex = "2980b9")
    {
        string tmp = markdown;

        // Bold: **text** ? <b>text</b>
        tmp = Regex.Replace(tmp, @"\*\*(.+?)\*\*", "<b>$1</b>");

        // Italic: *text* or _text_ ? <i>text</i>
        tmp = Regex.Replace(tmp, @"(\*|_)(.+?)\1", "<i>$2</i>");

        // Headings: # text ? <size=150%><b>text</b></size>
        tmp = Regex.Replace(tmp, @"^# (.+)$", "<cellCount=150%><b>$1</b></cellCount>", RegexOptions.Multiline);
        tmp = Regex.Replace(tmp, @"^## (.+)$", "<cellCount=125%><b>$1</b></cellCount>", RegexOptions.Multiline);
        tmp = Regex.Replace(tmp, @"^### (.+)$", "<cellCount=110%><b>$1</b></cellCount>", RegexOptions.Multiline);

        // Hyperlinks Stylization : [text](url) ? <color=#2980b9><u>text</u></color>
        tmp = Regex.Replace(tmp, @"\[(.+?)\]\((.+?)\)", $"<color=#{urlColorHex}><u>$1</u></color>");

        return tmp;
    }

    public static string ToRankingSuffix(int rank)
	{
		switch (rank)
		{
			case 1:
				return "st";
			case 2:
				return "nd";
			case 3:
				return "rd";
			default:
				return "th";
		}
	}
    public static string ToAlphabeticIndex(this int index, bool InMaj = true)
    {
        if (index < 1 || index > 26)
        {
            Debug.LogError("Number must be between 1 and 26.");
            return string.Empty;
        }
        if(InMaj) return ALPHABET_MAJ[index - 1].ToString();
        else return ALPHABET[index - 1].ToString();
    }
}
