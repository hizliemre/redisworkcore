using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using NRediSearch;
using RedisworkCore.Redisearch;

namespace RedisworkCore
{
	public enum RediState
	{
		Add,
		Update,
		Delete
	}

	public class ChangedEntry
	{
		public RediState State { get; set; }
		public string Key { get; set; }
		public Type EntityType { get; set; }
	}

	public abstract class Rediset
	{
		internal readonly List<Document> Addeds = new List<Document>();
		internal readonly List<string> Deleteds = new List<string>();
		internal readonly List<Document> Updateds = new List<Document>();
		internal Client Client;

		public List<ChangedEntry> ChangedEntries =>
			Addeds.Select(m => new ChangedEntry {State = RediState.Add, Key = m.Id, EntityType = EntityType})
				  .Union(Updateds.Select(m => new ChangedEntry {State = RediState.Update, Key = m.Id, EntityType = EntityType}))
				  .Union(Deleteds.Select(m => new ChangedEntry {State = RediState.Delete, Key = m, EntityType = EntityType}))
				  .ToList();

		internal abstract Type EntityType { get; }

		internal abstract void BuildIndex();
	}

	public class Rediset<T> : Rediset, IRedisearchQueryable<T>, IRedisearchTake<T>
		where T : class, new()
	{
		private const int _defaultTake = 1000000;
		private readonly RedisContext _context;
		private readonly object _locker = new object();
		private readonly List<RedisearchSortDescriptor> _sorts = new List<RedisearchSortDescriptor>();
		private int _skip;
		private int _take = _defaultTake;
		private string _whereQuery = string.Empty;

		public Rediset(RedisContext context)
		{
			_context = context;
			Client = new Client($"{typeof(T).FullName}_idx", _context.Database);
			_context.Trackeds.Add(this);
			EntityType = typeof(T);
		}

		internal override Type EntityType { get; }

		public IRedisearchQueryable<T> Where(Expression<Func<T, bool>> expression)
		{
			_whereQuery = expression.Where(_whereQuery);
			return this;
		}

		public IRedisearchQueryable<T> SortBy(Expression<Func<T, object>> propSelector)
		{
			propSelector.SortBy(_sorts);
			return this;
		}

		public IRedisearchQueryable<T> SortByDescending(Expression<Func<T, object>> propSelector)
		{
			propSelector.SortByDescending(_sorts);
			return this;
		}

		public IRedisearchQueryable<T> SortBy(string propertyName)
		{
			RedisearchQueryExecuter.SortBy<T>(propertyName, _sorts);
			return this;
		}

		public IRedisearchQueryable<T> SortByDescending(string propertyName)
		{
			RedisearchQueryExecuter.SortByDescending<T>(propertyName, _sorts);
			return this;
		}

		public bool Any(Expression<Func<T, bool>> expression)
		{
			string query = expression.Where();
			return Client.Any<T>(query);
		}

		public Task<long> CountAsync(Expression<Func<T, bool>> expression)
		{
			string query = expression.Where();
			return Client.CountAsync<T>(query);
		}

		public Task<long> CountAsync()
		{
			return Client.CountAsync();
		}

		public IRedisearchTake<T> Skip(int count)
		{
			_skip = count;
			return this;
		}

		public async Task<List<T>> ToListAsync()
		{
			List<T> list = await Client.ToListAsync<T>(_whereQuery, _sorts, _skip, _take);
			_whereQuery = string.Empty;
			_sorts.Clear();
			_skip = 0;
			_take = _defaultTake;
			return list;
		}

		public Task<List<T>> TakeAsync(int count)
		{
			if (count > 1000000)
				throw new NotSupportedException("Take limit exceeds maximum of 1.000.000");
			_take = count;
			return ToListAsync();
		}

		public void Add(T model)
		{
			Document doc = CreateDocument(model);
			lock (_locker)
			{
				Addeds.Add(doc);
			}
		}

		public void Add(params T[] models)
		{
			Document[] docs = models.Select(CreateDocument).ToArray();
			lock (_locker)
			{
				Addeds.AddRange(docs);
			}
		}

		public void Delete(T model)
		{
			Document doc = CreateDocument(model);
			lock (_locker)
			{
				Deleteds.Add(doc.Id);
			}
		}

		public void Delete(params T[] models)
		{
			string[] docIds = models.Select(CreateDocument)
									.Select(x => x.Id)
									.ToArray();
			lock (_locker)
			{
				Deleteds.AddRange(docIds);
			}
		}

		public void Update(params T[] models)
		{
			Document[] docs = models.Select(CreateDocument)
									.ToArray();
			lock (_locker)
			{
				Updateds.AddRange(docs);
			}
		}

		public Task<T> Find(params object[] keys)
		{
			return Client.Find<T>(keys);
		}

		public Task<T> FindByDocId(string docId)
		{
			return Client.FindByDocId<T>(docId);
		}

		public bool All(Expression<Func<T, bool>> expression)
		{
			string query = expression.Where(true);
			return Client.All<T>(query);
		}

		internal override void BuildIndex()
		{
			Client.CreateIndex<T>();
		}

		public void RebuildIndex()
		{
			bool indexExist = _context.Database.KeyExists($"idx:{Client.IndexName}");
			if (indexExist) Client.DropIndex();
			BuildIndex();
		}

		private static Document CreateDocument(T model)
		{
			string key = RedisContext.FindKey(model);

			PropertyInfo keyProp = Helpers.GetRedisKeyValueAttributes<T>();
			if (keyProp != null)
				keyProp.SetValue(model, key);

			Document doc = model.CreateDocument(key);
			return doc;
		}
	}
}