using System;
using System.Text;

namespace RE2Extended.Runtime.ConstructionManager
{
    internal static class ConstructionManagerTooltipText
    {
        internal static string ToUnityRichText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var builder = new StringBuilder(text.Length + 32);
            bool colorOpen = false;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '[')
                {
                    int end = text.IndexOf(']', i + 1);
                    if (end > i)
                    {
                        string tag = text.Substring(i + 1, end - i - 1);
                        if (tag == "c")
                        {
                            i = end + 1;
                            continue;
                        }
                        if (tag == "-" || tag == "/c")
                        {
                            if (colorOpen) builder.Append("</color>");
                            colorOpen = false;
                            i = end + 1;
                            continue;
                        }
                        if (IsHexColor(tag))
                        {
                            if (colorOpen) builder.Append("</color>");
                            builder.Append("<color=#").Append(tag.Substring(0, 6)).Append('>');
                            colorOpen = true;
                            i = end + 1;
                            continue;
                        }

                        switch (tag)
                        {
                            case "b": builder.Append("<b>"); break;
                            case "/b": builder.Append("</b>"); break;
                            case "i": builder.Append("<i>"); break;
                            case "/i": builder.Append("</i>"); break;
                            case "u": builder.Append("<u>"); break;
                            case "/u": builder.Append("</u>"); break;
                            case "s":
                            case "/s":
                            case "sup":
                            case "/sup":
                            case "sub":
                            case "/sub":
                                break;
                            default:
                                // Unknown NGUI tag: strip it rather than show markup.
                                break;
                        }
                        i = end + 1;
                        continue;
                    }
                }

                if (c == '|')
                {
                    int end = text.IndexOf('|', i + 1);
                    if (end == i + 3 && text.Substring(i + 1, 2) == "IS")
                    {
                        builder.Append('\u2022');
                        i = end + 1;
                        continue;
                    }
                }

                AppendEscaped(builder, c);
                i++;
            }

            if (colorOpen) builder.Append("</color>");
            return builder.ToString();
        }

        internal static string ToPlainText(string text)
        {
            string rich = ToUnityRichText(text);
            if (string.IsNullOrEmpty(rich)) return string.Empty;

            var builder = new StringBuilder(rich.Length);
            for (int i = 0; i < rich.Length; i++)
            {
                char c = rich[i];
                if (c == '<')
                {
                    int end = rich.IndexOf('>', i + 1);
                    if (end > i)
                    {
                        i = end;
                        continue;
                    }
                }
                builder.Append(c);
            }
            return builder.ToString();
        }

        private static bool IsHexColor(string tag)
        {
            if (tag == null || (tag.Length != 6 && tag.Length != 8)) return false;
            for (int i = 0; i < tag.Length; i++)
            {
                char c = tag[i];
                bool hex = (c >= '0' && c <= '9') ||
                           (c >= 'a' && c <= 'f') ||
                           (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        private static void AppendEscaped(StringBuilder builder, char c)
        {
            switch (c)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                default: builder.Append(c); break;
            }
        }

        internal static string FormatSeconds(float seconds)
        {
            if (seconds < 0f) return string.Empty;
            return seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
