using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace RedisworkCore.Redisearch
{
	public interface IRedisearchQueryable<T> : IRedisearchSkip<T>
	{
		public IRedisearchQueryable<T> Where(Expression<Func<T, bool>> expression);

		public IRedisearchQueryable<T> SortBy(Expression<Func<T, object>> propSelector);

		public IRedisearchQueryable<T> SortByDescending(Expression<Func<T, object>> propSelector);

		public bool Any(Expression<Func<T, bool>> expression);

		public Task<List<T>> ToListAsync();
	}

	public interface IRedisearchSkip<T>
	{
		public IRedisearchTake<T> Skip(int count);
	}

	public interface IRedisearchTake<T>
	{
		public Task<List<T>> TakeAsync(int count);

		public Task<List<T>> ToListAsync();
	}
}