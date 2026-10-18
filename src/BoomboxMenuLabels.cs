using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxMenuLabels
    {
        private readonly List<KeyValuePair<Action<string>, Func<string>>> entries =
            new List<KeyValuePair<Action<string>, Func<string>>>();
        private string language;

        internal T Add<T>(T text, Func<string> value) where T : TMP_Text
        {
            Register(v => text.text = v, value);
            return text;
        }
        internal Button Add(Button button, Func<string> value)
        {
            Register(v => BoomboxNativeUI.Label(button, v), value);
            return button;
        }
        internal TMP_InputField Add(TMP_InputField input, Func<string> value)
        {
            Register(v => BoomboxNativeControls.Placeholder(input, v), value);
            return input;
        }

        private void Register(Action<string> apply, Func<string> value)
        {
            entries.Add(new KeyValuePair<Action<string>, Func<string>>(apply, value));
            apply(value());
        }

        internal bool Refresh()
        {
            string current = Localization.instance.GetSelectedLanguage();
            if (current == language)
                return false;
            language = current;
            foreach (var entry in entries)
                entry.Key(entry.Value());
            return true;
        }
    }
}
