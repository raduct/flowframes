using Flowframes.Data;
using Flowframes.MiscUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Flowframes
{
    public static class ExtensionMethods
    {
        private static readonly Regex NotSignedDigitsOrComma = new Regex("[^\\-,0-9]");
        private static readonly Regex NotSignedDigitsOrCommaOrDot = new Regex("[^.,\\-,0-9]");

        public static string TrimNotNumbers(this string s, bool allowDotComma = false)
        {
            if (!allowDotComma)
                s = NotSignedDigitsOrComma.Replace(s, string.Empty);
            else
                s = NotSignedDigitsOrCommaOrDot.Replace(s, string.Empty);
            return s.Trim();
        }

        public static int GetInt(this TextBox textbox)
        {
            return GetInt(textbox.Text);
        }

        public static int GetInt(this ComboBox combobox)
        {
            return GetInt(combobox.Text);
        }

        public static int GetInt(this string str)
        {
            if (str == null || str.Length < 1 || str.Contains('\n') || str == "N/A" || !str.Any(char.IsDigit))
                return 0;

            try
            {
                return int.Parse(str.TrimNotNumbers());
            }
            catch (Exception e)
            {
                Logger.Log("Failed to parse \"" + str + "\" to int: " + e.Message, true);
                return 0;
            }
        }

        public static bool GetBool(this string str)
        {
            try
            {
                return bool.Parse(str);
            }
            catch
            {
                return false;
            }
        }

        public static float GetFloat(this TextBox textbox)
        {
            return GetFloat(textbox.Text);
        }

        public static float GetFloat(this ComboBox combobox)
        {
            return GetFloat(combobox.Text);
        }

        public static float GetFloat(this string str)
        {
            if (str == null || str.Length < 1)
                return 0f;

            string num = str.Replace(",", ".").TrimNotNumbers(true);
            _ = float.TryParse(num, out float value);
            return value;
        }

        public static string Wrap(this string path, bool addSpaceFront = false, bool addSpaceEnd = false)
        {
            string s = "\"" + path + "\"";

            if (addSpaceFront)
                s = " " + s;

            if (addSpaceEnd)
                s += " ";

            return s;
        }

        public static string GetParentDir(this string path)
        {
            return Directory.GetParent(path).FullName;
        }

        public static int RoundToInt(this float f)
        {
            return (int)Math.Round(f);
        }

        public static int Clamp(this int i, int min, int max)
        {
            if (i < min)
                i = min;

            if (i > max)
                i = max;

            return i;
        }

        public static float Clamp(this float i, float min, float max)
        {
            if (i < min)
                i = min;

            if (i > max)
                i = max;

            return i;
        }

        private static readonly Regex EOLRegex = new Regex("\r\n|\r|\n");
        public static string[] SplitIntoLines(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return Array.Empty<string>();

            return EOLRegex.Split(str);
        }

        public static string Trunc(this string inStr, int maxChars, bool addEllipsis = true)
        {
            string str = inStr.Length <= maxChars ? inStr : inStr.Substring(0, maxChars);
            if (addEllipsis && inStr.Length > maxChars)
                str += "…";
            return str;
        }

        private static readonly Regex BadCharsRegex = new Regex(@"[^\u0020-\u007E]");
        public static string StripBadChars(this string str)
        {
            string outStr = BadCharsRegex.Replace(str, string.Empty);
            outStr = outStr.Remove("(").Remove(")").Remove("[").Remove("]").Remove("{").Remove("}").Remove("%").Remove("'").Remove("~");
            return outStr;
        }

        public static string StripNumbers(this string str)
        {
            return new string(str.Where(c => c != '-' && (c < '0' || c > '9')).ToArray());
        }

        public static string Remove(this string str, string stringToRemove)
        {
            if (str == null || stringToRemove == null)
                return string.Empty;

            return str.Replace(stringToRemove, string.Empty);
        }

        public static string TrimWhitespaces(this string str)
        {
            if (str == null) return string.Empty;
            var newString = new StringBuilder();
            bool previousIsWhitespace = false;
            for (int i = 0; i < str.Length; i++)
            {
                if (Char.IsWhiteSpace(str[i]))
                {
                    if (previousIsWhitespace)
                        continue;
                    previousIsWhitespace = true;
                }
                else
                {
                    previousIsWhitespace = false;
                }
                newString.Append(str[i]);
            }
            return newString.ToString();
        }

        public static string ReplaceLast(this string str, string stringToReplace, string replaceWith)
        {
            int place = str.LastIndexOf(stringToReplace);

            if (place == -1)
                return str;

            return str.Remove(place, stringToReplace.Length).Insert(place, replaceWith);
        }

        public static string RemoveComments(this string str)
        {
            return str.Split('#')[0].Split("//")[0];
        }

        public static string FilenameSuffix(this string path, string suffix)
        {
            string filename = Path.ChangeExtension(path, null);
            string ext = Path.GetExtension(path);
            return filename + suffix + ext;
        }

        public static string ToStringDot(this float f, string format = "")
        {
            if (string.IsNullOrWhiteSpace(format))
                return f.ToString().Replace(",", ".");
            else
                return f.ToString(format).Replace(",", ".");
        }

        public static string[] Split(this string str, string trimStr)
        {
            return str?.Split(new string[] { trimStr }, StringSplitOptions.None);
        }

        public static bool MatchesWildcard(this string str, string wildcard)
        {
            string pattern = Regex.Escape(wildcard).Replace("\\*", ".*?");
            return Regex.IsMatch(str, pattern);
        }

        public static string ToTitleCase(this string s)
        {
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s);
        }

        public static string ToStringShort(this Size s, string separator = "x")
        {
            return $"{s.Width}{separator}{s.Height}";
        }

        public static bool IsConcatFile(this string filePath)
        {
            try
            {
                return Path.GetExtension(filePath)?.ToLowerInvariant() == ".concat";
            }
            catch
            {
                return false;
            }
        }

        public static string GetConcStr(this string filePath, int rate = -1)
        {
            string rateStr = rate >= 0 ? $"-r {rate} " : string.Empty;
            return filePath.IsConcatFile() ? $"{rateStr}-safe 0 -f concat " : string.Empty;
        }

        public static string GetFfmpegInputArg(this string filePath)
        {
            return $"{(filePath.IsConcatFile() ? filePath.GetConcStr() : string.Empty)} -i {filePath.Wrap()}";
        }

        public static string Get(this Dictionary<string, string> dict, string key, bool returnKeyInsteadOfEmptyString = false, bool ignoreCase = false)
        {
            if (key == null)
                key = string.Empty;

            for (int i = 0; i < dict.Count; i++)
            {
                if (ignoreCase)
                {
                    if (key.Lower() == dict.ElementAt(i).Key.Lower())
                        return dict.ElementAt(i).Value;
                }
                else
                {
                    if (key == dict.ElementAt(i).Key)
                        return dict.ElementAt(i).Value;
                }
            }

            if (returnKeyInsteadOfEmptyString)
                return key;
            else
                return string.Empty;
        }

        public static void FillFromEnum<TEnum>(this ComboBox comboBox, Dictionary<string, string> stringMap = null, int defaultIndex = -1, List<TEnum> exclusionList = null) where TEnum : Enum
        {
            if (exclusionList == null)
                exclusionList = new List<TEnum>();

            var entriesToAdd = Enum.GetValues(typeof(TEnum)).Cast<TEnum>().Except(exclusionList);
            var strings = entriesToAdd.Select(x => stringMap.Get(x.ToString(), true));
            comboBox.FillFromStrings(strings, stringMap, defaultIndex);
        }

        public static void FillFromEnum<TEnum>(this ComboBox comboBox, IEnumerable<TEnum> entries, Dictionary<string, string> stringMap = null, int defaultIndex = -1) where TEnum : Enum
        {
            var strings = entries.Select(x => stringMap.Get(x.ToString(), true));
            comboBox.FillFromStrings(strings, stringMap, defaultIndex);
        }

        public static void FillFromEnum<TEnum>(this ComboBox comboBox, IEnumerable<TEnum> entries, Dictionary<string, string> stringMap, TEnum defaultEntry) where TEnum : Enum
        {
            if (stringMap == null)
                stringMap = new Dictionary<string, string>();

            comboBox.Items.Clear();
            comboBox.Items.AddRange(entries.Select(x => stringMap.Get(x.ToString(), true)).ToArray());
            comboBox.Text = stringMap.Get(defaultEntry.ToString(), true);
        }

        public static void FillFromStrings(this ComboBox comboBox, IEnumerable<string> entries, Dictionary<string, string> stringMap = null, int defaultIndex = -1, IEnumerable<string> exclusionList = null)
        {
            if (stringMap == null)
                stringMap = new Dictionary<string, string>();

            if (exclusionList == null)
                exclusionList = new List<string>();

            comboBox.Items.Clear();
            comboBox.Items.AddRange(entries.Select(x => stringMap.Get(x, true)).Except(exclusionList).ToArray());

            if (defaultIndex >= 0 && comboBox.Items.Count > 0)
                comboBox.SelectedIndex = defaultIndex;
        }

        public static void SetIfTextMatches(this ComboBox comboBox, string str, bool ignoreCase = true, Dictionary<string, string> stringMap = null)
        {
            if (stringMap == null)
                stringMap = new Dictionary<string, string>();

            str = stringMap.Get(str, true, true);

            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (ignoreCase)
                {
                    if (comboBox.Items[i].ToString().Lower() == str.Lower())
                    {
                        comboBox.SelectedIndex = i;
                        return;
                    }
                }
                else
                {
                    if (comboBox.Items[i].ToString() == str)
                    {
                        comboBox.SelectedIndex = i;
                        return;
                    }
                }
            }
        }

        public static string Lower(this string s)
        {
            if (s == null)
                return s;

            return s.ToLowerInvariant();
        }

        public static string Upper(this string s)
        {
            if (s == null)
                return s;

            return s.ToUpperInvariant();
        }

        public static EncoderInfoVideo GetInfo(this Enums.Encoding.Encoder enc)
        {
            return OutputUtils.GetEncoderInfoVideo(enc);
        }

        public static bool IsEmpty(this string s)
        {
            return string.IsNullOrWhiteSpace(s);
        }

        public static bool IsNotEmpty(this string s)
        {
            return !string.IsNullOrWhiteSpace(s);
        }

        public static bool Contains(this string s, string toCheck, StringComparison comp)
        {
            if (s == null) return false;
            return s.IndexOf(toCheck, comp) >= 0;
        }

        public static string ToJson(this object o, bool indent = false, bool ignoreErrors = true)
        {
            var settings = new JsonSerializerSettings();

            if (ignoreErrors)
                settings.Error = (s, e) => { e.ErrorContext.Handled = true; };

            // Serialize enums as strings.
            settings.Converters.Add(new StringEnumConverter());

            return JsonConvert.SerializeObject(o, indent ? Formatting.Indented : Formatting.None, settings);
        }
    }
}
