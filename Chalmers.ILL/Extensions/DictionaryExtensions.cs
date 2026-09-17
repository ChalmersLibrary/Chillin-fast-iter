using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Extensions
{
    public static class DictionaryExtensions
    {
        public static V GetValue<T,V>(this IDictionary<T,V> dict, T key) where V : class
        {
            V ret = null;
            dict.TryGetValue(key, out ret);
            return ret;
        }

        public static string GetValueString<T,V>(this IDictionary<T,V> dict, T key) where V : class
        {
            string ret = "";
            V temp = null;
            dict.TryGetValue(key, out temp);
            if (temp != null)
            {
                ret = temp.ToString();
            }
            return ret;
        }
    }
}