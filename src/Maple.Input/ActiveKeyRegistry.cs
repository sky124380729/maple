using System;
using System.Collections.Generic;
using System.Linq;

namespace Maple.Input
{
    public sealed class ActiveKeyRegistry
    {
        private readonly HashSet<string> activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IList<string> ActiveKeys { get { return activeKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly(); } }

        public bool KeyDown(string key)
        {
            ValidateKey(key);
            return activeKeys.Add(key);
        }

        public bool KeyUp(string key)
        {
            ValidateKey(key);
            return activeKeys.Remove(key);
        }

        public IList<string> ReleaseAll()
        {
            var released = ActiveKeys;
            activeKeys.Clear();
            return released;
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 32) throw new ArgumentException("按键名称无效", "key");
        }
    }
}
