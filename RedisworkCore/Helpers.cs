using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RedisworkCore.DataAnnotations;

namespace RedisworkCore
{
	public static class Helpers
	{
		public const string TagSeperator = "~";
		public const string NullString = "___|null|___";
		public const string EmptyString = "___|empty|___";

		public static string TagString(this string text)
		{
			return text.ToLower();
		}

		public static string ReverseString(this string text)
		{
			if (text == NullString) return string.Empty;
			text = text.TagString();
			char[] charArray = text.ToCharArray();
			Array.Reverse(charArray);
			return new string(charArray);
		}

		public static string SubsetString(this string text)
		{
			if (text == NullString) return string.Empty;
			text = text.TagString();
			List<string> subsets = new List<string>();
			for (int i = 2; i < text.Length; i++)
			for (int j = 0; j < text.Length - i; j++)
				subsets.Add(text.Substring(j, i + 1));
			string result = string.Join(TagSeperator, subsets.Distinct());
			return result;
		}

		public static PropertyInfo GetRedisKeyValueAttributes<T>()
		{
			PropertyInfo[] keyAttrs = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
			                                   .Where(x => x.IsDefined(typeof(RedisKeyValueAttribute)))
			                                   .ToArray();

			if (keyAttrs.Length > 1) throw new InvalidOperationException("Model should have only one RedisKeyValueAttribute.");
			if (keyAttrs.Length == 1 && keyAttrs[0].PropertyType != typeof(string)) throw new InvalidOperationException("Model key property type should be string.");
			return keyAttrs.Length == 0 ? null : keyAttrs[0];
		}

		public static PropertyInfo[] GetModelProperties<T>()
		{
			return typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(x => !x.IsDefined(typeof(RedisIgnoreAttribute))).ToArray();
		}
	}
}