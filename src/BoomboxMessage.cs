using System;

namespace VikingBoombox
{
    public sealed class BoomboxMessage
    {
        private readonly string key;
        private readonly object[] arguments;
        private readonly string literal;

        public BoomboxMessage(string key, params object[] arguments)
        {
            this.key = key ?? throw new ArgumentNullException(nameof(key));
            this.arguments = arguments == null || arguments.Length == 0
                ? Array.Empty<object>()
                : (object[])arguments.Clone();
        }

        private BoomboxMessage(string literal)
        {
            this.literal = literal;
        }

        public static BoomboxMessage Raw(string value) => new BoomboxMessage(value);

        public override string ToString()
        {
            if (key == null)
                return literal;
            if (arguments.Length == 0)
                return BoomboxText.Get(key);
            return BoomboxText.Format(key, arguments);
        }
    }
}
