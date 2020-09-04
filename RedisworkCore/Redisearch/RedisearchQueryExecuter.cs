using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NRediSearch;
using NRediSearch.Aggregation;
using RedisworkCore.Converters;
using RedisworkCore.DataAnnotations;
using StackExchange.Redis;

namespace RedisworkCore.Redisearch
{
	public static class RedisearchQueryExecuter
	{
		internal static async Task<T> Find<T>(this Client client, params object[] keys) where T : class
		{
			string key = RedisContext.GenerateKey(typeof(T), keys);
			Document retObj = await client.GetDocumentAsync(key);
			if (retObj is null) return null;
			string serialized = JsonConvert.SerializeObject(retObj.GetProperties());
			return JsonConvert.DeserializeObject<T>(serialized, RedisearchSerializerSettings.SerializerSettings);
		}

		internal static string Where<T>(this Expression<Func<T, bool>> expression, bool not = false)
		{
			string query = expression.ToRedisearchQuery(not);
			return query;
		}

		internal static string Where<T>(this Expression<Func<T, bool>> expression, string whereQuery)
		{
			string query = expression.ToRedisearchQuery();
			return $"{whereQuery} {query}";
		}

		internal static void SortBy<T>(this Expression<Func<T, object>> propSelector, List<RedisearchSortDescriptor> sorts)
		{
			string propName = propSelector.GetPropertyName();
			sorts.Add(new RedisearchSortDescriptor {Ascending = true, PropertyName = propName});
		}

		internal static void SortByDescending<T>(this Expression<Func<T, object>> propSelector, List<RedisearchSortDescriptor> sorts)
		{
			string propName = propSelector.GetPropertyName();
			sorts.Add(new RedisearchSortDescriptor {Ascending = false, PropertyName = propName});
		}

		internal static bool Any<T>(this Client client, string whereQuery)
		{
			if (string.IsNullOrEmpty(whereQuery)) throw new InvalidOperationException("Filter expression cannot be empty.");
			return client.Search(new Query(whereQuery))
			             .Documents
			             .Any();
		}

		internal static bool All<T>(this Client client, string whereQuery)
		{
			if (string.IsNullOrEmpty(whereQuery)) throw new InvalidOperationException("Filter expression cannot be empty.");
			return client.Search(new Query(whereQuery))
			             .Documents
			             .Any();
		}

		internal static long Count(this Client client)
		{
			return client.Search(new Query("*").Limit(0, 1000000))
			             .TotalResults;
		}

		internal static Task<List<T>> ToListAsync<T>(this Client client, string whereQuery, List<RedisearchSortDescriptor> sorts, int skip, int take)
		{
			whereQuery = string.IsNullOrEmpty(whereQuery) ? "*" : whereQuery.TrimStart();
			if (sorts.Count > 1) return client.ToListWithMultipleSortAsync<T>(whereQuery, skip, take, sorts);
			return client.ToListWithSingleSortAsync<T>(whereQuery, skip, take, sorts.FirstOrDefault());
		}

		private static async Task<List<T>> ToListWithSingleSortAsync<T>(this Client client, string whereQuery, int skip, int take, RedisearchSortDescriptor sort = null)
		{
			Query query = new Query(whereQuery);
			if (sort != null)
				query = query.SetSortBy(sort.PropertyName, sort.Ascending);

			PropertyInfo[] props = Helpers.GetModelProperties<T>();
			string[] returnFields = props.Select(x => x.Name).ToArray();
			query = query.Limit(skip, take).ReturnFields(returnFields);
			SearchResult queryResult = await client.SearchAsync(query);
			if (queryResult is null) return new List<T>();
			return queryResult.Documents.Select(x =>
			                  {
				                  string serialized = JsonConvert.SerializeObject(x.GetProperties());
				                  T deserialized = JsonConvert.DeserializeObject<T>(serialized, RedisearchSerializerSettings.SerializerSettings);
				                  return deserialized;
			                  })
			                  .ToList();
		}

		private static async Task<List<T>> ToListWithMultipleSortAsync<T>(this Client client, string whereQuery, int skip, int take, List<RedisearchSortDescriptor> sorts)
		{
			PropertyInfo[] props = Helpers.GetModelProperties<T>();
			SortedField[] sort = sorts.Select(x => new SortedField($"@{x.PropertyName}", x.Ascending ? Order.Ascending : Order.Descending))
			                          .ToArray();
			string[] returnFields = props.Select(x => x.Name).ToArray();
			AggregationBuilder aggregation = new AggregationBuilder(whereQuery).Load(returnFields)
			                                                                   .SortBy(sort)
			                                                                   .Limit(skip, take);
			AggregationResult aggreagate = await client.AggregateAsync(aggregation);
			IReadOnlyList<Dictionary<string, RedisValue>> aggregationResult = aggreagate.GetResults();
			return aggregationResult.Select(x =>
			                        {
				                        string serialized = JsonConvert.SerializeObject(x);
				                        T deserialized = JsonConvert.DeserializeObject<T>(serialized, RedisearchSerializerSettings.SerializerSettings);
				                        return deserialized;
			                        })
			                        .ToList();
		}

		internal static Document CreateDocument<T>(this T model, string key)
		{
			PropertyInfo[] props = Helpers.GetModelProperties<T>();
			Document doc = new Document(key);
			foreach (PropertyInfo prop in props)
			{
				object value = prop.GetValue(model) ?? Expression.Lambda(Expression.Default(prop.PropertyType)).Compile().DynamicInvoke();

				if (prop.PropertyType.IsGenericType)
				{
					doc.CreateDocumentFromGenericType(prop, model);
					continue;
				}

				if (prop.PropertyType == typeof(decimal))
				{
					doc.Set(prop.Name, (string) value);
					continue;
				}

				if (prop.PropertyType == typeof(string) || prop.PropertyType.IsValueType)
				{
					if (value != null) doc.Set(prop.Name, (dynamic) value);
					if (prop.PropertyType == typeof(string) && !prop.IsDefined(typeof(RedisKeyValueAttribute)))
					{
						string sVal = (string) value;
						
						if (sVal is null) sVal = Helpers.NullString;
						else if (sVal == string.Empty) sVal = Helpers.EmptyString;
						
						doc.Set($"{prop.Name}_tag", sVal.TagString());
						doc.Set($"{prop.Name}_reverse_tag", sVal.ReverseString());
						doc.Set($"{prop.Name}_subset_tag", sVal.SubsetString());
					}

					continue;
				}

				throw new NotSupportedException();
			}

			return doc;
		}

		private static void CreateDocumentFromGenericType<T>(this Document doc, PropertyInfo prop, T model)
		{
			if (prop.PropertyType.GenericTypeArguments[0] == typeof(string) || prop.PropertyType.GenericTypeArguments[0] is { IsValueType: true })
			{
				IList values = prop.GetValue(model) as IList;
				if (values is null) return;

				List<string> _join = new List<string>();
				foreach (object value in values)
					_join.Add(value.ToString());
				string tags = string.Join(Helpers.TagSeperator, _join);
				doc.Set(prop.Name, tags);
				return;
			}

			throw new NotSupportedException();
		}

		private static string GetPropertyName<T>(this Expression<Func<T, object>> expression)
		{
			if (expression.Body is MemberExpression memberEx)
				return memberEx.Member.Name;
			if (expression.Body is UnaryExpression unaryEx && unaryEx.Operand is MemberExpression unaryMember)
				return unaryMember.Member.Name;

			throw new InvalidOperationException();
		}
	}
}