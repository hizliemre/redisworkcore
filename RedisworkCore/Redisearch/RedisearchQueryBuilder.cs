using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using NRediSearch;
using RedisworkCore.DataAnnotations;

namespace RedisworkCore.Redisearch
{
	public static class RedisearchQueryBuilder
	{
		internal static string ToRedisearchQuery<T>(this Expression<Func<T, bool>> expression, bool not = false)
		{
			Expression exp = expression;
			if (not) exp = Expression.Not(exp);
			return SerializeInternal(exp);
		}

		internal static void CreateIndex<T>(this Client client)
		{
			PropertyInfo[] props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
											.Where(x => !x.IsDefined(typeof(RedisKeyValueAttribute)))
											.ToArray();
			Schema scheme = new Schema();
			foreach (PropertyInfo prop in props)
				if (prop.PropertyType.IsGenericType && (prop.PropertyType.GenericTypeArguments[0] == typeof(string) || prop.PropertyType.GenericTypeArguments[0] is { IsValueType: true }))
					scheme.AddTagField(prop.Name, Helpers.TagSeperator);
				else if (prop.PropertyType == typeof(string) || prop.PropertyType.IsValueType)
					prop.BuildField(scheme);

			PropertyInfo keyProperty = Helpers.GetRedisKeyValueAttributes<T>();
			if (keyProperty != null) BuildField(keyProperty, scheme);

			Client.ConfiguredIndexOptions indexOptions = new Client.ConfiguredIndexOptions(Client.IndexOptions.DisableStopWords | Client.IndexOptions.UseTermOffsets);
			client.CreateIndex(scheme, indexOptions);
		}

		private static void BuildField(this PropertyInfo propertyInfo, Schema scheme)
		{
			if (propertyInfo.IsDefined(typeof(RedisKeyValueAttribute)))
			{
				scheme.AddField(new Schema.TextField(propertyInfo.Name, sortable: false, noIndex: true));
				return;
			}

			switch (propertyInfo.PropertyType.Name)
			{
				case "String":
					scheme.AddSortableTextField(propertyInfo.Name);
					scheme.AddTagField($"{propertyInfo.Name}_tag", Helpers.TagSeperator);
					scheme.AddTagField($"{propertyInfo.Name}_reverse_tag", Helpers.TagSeperator);
					scheme.AddTagField($"{propertyInfo.Name}_subset_tag", Helpers.TagSeperator);
					break;
				case "Decimal":
					scheme.AddTextField(propertyInfo.Name);
					break;
				case "Byte":
				case "Int64":
				case "Int32":
				case "Int16":
				case "UInt64":
				case "UInt32":
				case "UInt16":
				case "Double":
				case "Boolean":
					scheme.AddSortableNumericField(propertyInfo.Name);
					break;
			}
		}

		private static string SerializeInternal(Expression exp, RedisearchNodeType? nodeType = null)
		{
			if (exp is LambdaExpression lambdaEx)
				return SerializeLambda(lambdaEx);
			if (exp is BinaryExpression binaryEx)
				return SerializeBinary(binaryEx.Left, binaryEx.Right, nodeType ?? (RedisearchNodeType) binaryEx.NodeType);
			if (exp is MemberExpression memberEx)
				return SerializeMember(memberEx, nodeType);
			if (exp is ConstantExpression constatEx)
				return SerializeConstant(constatEx, nodeType);
			if (exp is MethodCallExpression methodCallEx)
				return SerializeMethodCall(methodCallEx);

			return "*";
		}

		private static string SerializeMethodCall(MethodCallExpression methodCallEx)
		{
			if (methodCallEx.Arguments[0] is MemberExpression)
			{
				/*
					x => x.ModelValueTypeList.Contains(1)
					----------
					var list = new List<int> { 1, 2, 3 };
					x => list.Contains(x.ModelProperty);
				*/

				RedisearchNodeType nodeType = methodCallEx.Method.Name switch
				{
					nameof(string.Contains) => RedisearchNodeType.Contains,
					_                       => throw new NotSupportedException()
				};

				ConstantExpression constantEx = FindConstantExpression(methodCallEx.Object);
				return SerializeBinary(methodCallEx.Arguments[0], constantEx, nodeType);
			}


			if (methodCallEx.Object is MemberExpression)
			{
				/*
					x => x.ModelProperty.StartsWith("d")
					---------
					var d = "d";
					x => x.ModelProperty.StartsWith(d)
				*/

				/*
					x => x.ModelProperty.EndsWith("d")
					---------
					var d = "d";
					x => x.ModelProperty.EndsWith(d)
				*/

				/*
					x => x.ModelProperty.Contains(1)
					----------
					var d = 1;
				    x => x.ModelProperty.Contains(d)
				*/

				RedisearchNodeType nodeType = methodCallEx.Method.Name switch
				{
					nameof(string.StartsWith) => RedisearchNodeType.StartsWith,
					nameof(string.EndsWith)   => RedisearchNodeType.EndsWith,
					nameof(string.Contains)   => RedisearchNodeType.Contains,
					_                         => throw new NotSupportedException()
				};

				ConstantExpression constantEx = FindConstantExpression(methodCallEx.Arguments[0]);
				return SerializeBinary(methodCallEx.Object, constantEx, nodeType);
			}


			if (methodCallEx.Object is MethodCallExpression innerMethodCall && innerMethodCall.Object is MemberExpression leftExp)
				if (methodCallEx.Arguments[0] is MethodCallExpression)
				{
					RedisearchNodeType nodeType = methodCallEx.Method.Name switch
					{
						nameof(string.StartsWith) => RedisearchNodeType.StartsWith,
						nameof(string.EndsWith)   => RedisearchNodeType.EndsWith,
						nameof(string.Contains)   => RedisearchNodeType.Contains,
						_                         => throw new NotSupportedException()
					};

					ConstantExpression constantEx = FindConstantExpression(methodCallEx.Arguments[0]);
					return SerializeBinary(leftExp, constantEx, nodeType);
				}

			throw new NotSupportedException();
		}

		private static string SerializeConstant(ConstantExpression constatEx, RedisearchNodeType? nodeType)
		{
			if (!nodeType.HasValue) throw new InvalidOperationException();

			if (constatEx.Type.IsGenericType)
			{
				/*
					 -- string --
					var list = new List<string> { "a", "b", "c" }
					LAMBDA : x => list.Contains(x.ModelProperty) 
					QUERY  : @ModelProperty:('a'|'b'|'c')
					---------------------------------------
					
					-- numeric --
					var list = new List<int> { 1, 2, 3 }
					LAMBDA : x => list.Contains(x.ModelProperty) 
					QUERY  : @ModelProperty:[1 1]|@ModelProperty:[2 2]|@ModelProperty:[3 3]
					-----------------------------------------------
				 */
				Type genericArgumentType = constatEx.Type.GenericTypeArguments[0];
				IList values = (IList) constatEx.Value;
				List<string> query = new List<string>();
				foreach (object value in values)
					query.Add(SerializeConstant(genericArgumentType.Name, value, nodeType));
				// return genericArgumentType.Name == "String" ? $"{{0}}:({string.Join('|', query)})" : string.Join('|', query);
				return genericArgumentType.Name == "String" ? $"{{0}}_subset_tag:{{{{{string.Join('|', query)}}}}}" : string.Join('|', query);
			}

			if (nodeType == RedisearchNodeType.Contains)
				return $"{{0}}_subset_tag:{{{{{constatEx.Value}}}}}";
			// return $"{{0}}:({constatEx.Value})";

			return SerializeConstant(constatEx.Type.Name, constatEx.Value, nodeType);
		}

		private static string SerializeConstant(string typeName, object value, RedisearchNodeType? nodeType)
		{
			string numericFormat = nodeType switch
			{
				RedisearchNodeType.Equal              => "{{0}}:[{0} {0}]",
				RedisearchNodeType.NotEqual           => "-{{0}}:[{0} {0}]",
				RedisearchNodeType.GreaterThan        => "[({0} +inf]",
				RedisearchNodeType.GreaterThanOrEqual => "[{0} +inf]",
				RedisearchNodeType.LessThan           => "[-inf ({0}]",
				RedisearchNodeType.LessThanOrEqual    => "[-inf {0}]",
				RedisearchNodeType.Contains           => "{{0}}:[{0} {0}]",
				_                                     => string.Empty
			};

			string stringFormat = nodeType switch
			{
				RedisearchNodeType.Equal => "{{0}}_tag:{{{{{0}}}}}",
				// RedisearchNodeType.Equal      => "({0})",
				RedisearchNodeType.NotEqual => "-{{0}}_tag:{{{{{0}}}}}",
				// RedisearchNodeType.NotEqual   => "({0})",
				RedisearchNodeType.StartsWith => "{{0}}_tag:{{{{{0}*}}}}",
				// RedisearchNodeType.StartsWith => "({0}*)",
				RedisearchNodeType.EndsWith => "{{0}}_reverse_tag:{{{{{0}*}}}}",
				RedisearchNodeType.Contains => "{0}",
				// RedisearchNodeType.Contains => "'{0}'",
				_ => string.Empty
			};

			switch (typeName)
			{
				case "Byte":
				case "Int64":
				case "Int32":
				case "Int16":
				case "UInt64":
				case "UInt32":
				case "UInt16":
				case "Double":
					if (string.IsNullOrEmpty(numericFormat)) throw new InvalidOperationException();
					return string.Format(numericFormat, value);
				case "Boolean":
				case "String":
					if (string.IsNullOrEmpty(stringFormat)) throw new InvalidOperationException();
					return string.Format(stringFormat, EscapeTokenizedChars((string) value, nodeType == RedisearchNodeType.EndsWith));
			}

			throw new NotSupportedException();
		}

		private static string EscapeTokenizedChars(string text, bool reverse)
		{
			text = text.ToLower();
			if (reverse) text = text.ReverseString();
			Regex regex = new Regex(@"[\,\.\<\>\{\}\[\]\""\'\:\;\!\@\#\$\%\^\&\*\(\)\-\+\=\~\s]");
			return regex.Replace(text, m => $"\\{m}");
		}

		private static string SerializeMember(MemberExpression memberEx, RedisearchNodeType? nodeType = null)
		{
			if (memberEx.Expression is MemberExpression)
			{
				ConstantExpression constantEx = FindConstantExpression(memberEx);
				return SerializeConstant(constantEx, nodeType);
			}

			return $"@{memberEx.Member.Name}";
		}

		private static string SerializeBinary(Expression leftEx, Expression rightEx, RedisearchNodeType nodeType)
		{
			string left = leftEx is BinaryExpression ? SerializeInternal(leftEx) : SerializeInternal(leftEx, nodeType);
			string right = rightEx is BinaryExpression ? SerializeInternal(rightEx) : SerializeInternal(rightEx, nodeType);
			return nodeType switch
			{
				RedisearchNodeType.AndAlso => $"({left} {right})",
				RedisearchNodeType.OrElse  => $"({left}|{right})",
				RedisearchNodeType.Equal   => $"({string.Format(right, left)})",
				// RedisearchNodeType.Equal              => $"({left}:{right})",
				RedisearchNodeType.NotEqual => $"({string.Format(right, left)})",
				// RedisearchNodeType.NotEqual           => $"-({left}:{right})",
				RedisearchNodeType.GreaterThan        => $"({left}:{right})",
				RedisearchNodeType.GreaterThanOrEqual => $"({left}:{right})",
				RedisearchNodeType.LessThan           => $"({left}:{right})",
				RedisearchNodeType.LessThanOrEqual    => $"({left}:{right})",
				RedisearchNodeType.StartsWith         => $"({string.Format(right, left)})",
				// RedisearchNodeType.StartsWith         => $"({left}:{right})",
				RedisearchNodeType.EndsWith => $"({string.Format(right, left)})",
				RedisearchNodeType.Contains => $"({string.Format(right, left)})",
				_                           => throw new NotSupportedException()
			};
		}

		private static string SerializeLambda(LambdaExpression lambdaEx)
		{
			return SerializeInternal(lambdaEx.Body);
		}

		private static ConstantExpression FindConstantExpression(Expression expression)
		{
			if (expression is ConstantExpression constant) return constant;
			if (expression is MemberExpression innerMemberEx)
			{
				Expression exp = innerMemberEx;
				while (exp is MemberExpression innerMember)
					switch (innerMember.Expression)
					{
						case MemberExpression _:
							exp = innerMember.Expression;
							break;
						case ConstantExpression _:
						{
							object value = Expression.Lambda(innerMemberEx)
													 .Compile()
													 .DynamicInvoke();
							return Expression.Constant(value);
						}
					}
			}

			if (expression is MethodCallExpression methodCallEx)
			{
				object value = Expression.Lambda(methodCallEx)
										 .Compile()
										 .DynamicInvoke();
				return Expression.Constant(value);
			}

			if (expression is ListInitExpression listInitEx)
			{
				var result = Expression.Lambda(listInitEx).Compile().DynamicInvoke();
				return Expression.Constant(result);
			}

			throw new NotSupportedException();
		}
	}
}