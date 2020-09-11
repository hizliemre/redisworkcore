using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NRediSearch;
using Polly;
using Polly.Retry;
using RedisworkCore.DataAnnotations;
using StackExchange.Redis;

namespace RedisworkCore
{
	public abstract class RedisContext : IDisposable
	{
		private readonly RedisContextOptions _options;
		private readonly Dictionary<Type, Rediset> _sets = new Dictionary<Type, Rediset>();
		private readonly ReaderWriterLockSlim _locker = new ReaderWriterLockSlim();
		internal readonly List<Rediset> Trackeds = new List<Rediset>();
		private ConnectionMultiplexer _redis;
		internal IDatabase Database;
		public bool TransactionStarted;

		protected RedisContext(RedisContextOptions options)
		{
			_options = options;
			Connect();
			SetContext();
		}

		public void BuildIndex()
		{
			Database.Execute("FLUSHALL");
			_sets.Clear();
			SetContext(true);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		public async Task BeginTransactionAsync()
		{
			if (TransactionStarted) return;
			TransactionStarted = true;
			await Database.ExecuteAsync("MULTI");
		}

		public async Task SaveChangesAsync()
		{
			foreach (Rediset set in Trackeds)
			{
				foreach (string docId in set.Deleteds)
					await Database.ExecuteAsync("FT.DEL", set.Client.IndexName, docId, "DD");
				
				set.Deleteds.Clear();
				await set.Client.AddDocumentsAsync(new AddOptions
				{
					ReplacePolicy = AddOptions.ReplacementPolicy.Full,
				}, set.AddOrUpdateds.ToArray());
				set.AddOrUpdateds.Clear();
			}
		}

		public async Task CommitTransactionAsync()
		{
			if (TransactionStarted)
			{
				await Database.ExecuteAsync("EXEC");
				TransactionStarted = false;
			}
		}

		public async Task RollbackTransaction()
		{
			if (TransactionStarted)
				await Database.ExecuteAsync("DISCARD");
		}

		public Rediset<T> Set<T>() where T : class, new()
		{
			try
			{
				_locker.EnterReadLock();
				if (_sets.ContainsKey(typeof(T))) return (Rediset<T>) _sets[typeof(T)];
			}
			finally
			{
				_locker.ExitReadLock();
			}

			throw new InvalidOperationException($"{nameof(T)} is not defined to context");
		}

		internal static string FindKey(object model)
		{
			Type type = model.GetType();
			var props = type.GetProperties()
			                .Where(x => x.IsDefined(typeof(RedisKeyAttribute)))
			                .Select(x => new
			                {
				                Prop = x, x.GetCustomAttribute<RedisKeyAttribute>()
				                           ?.Order
			                })
			                .OrderBy(x => x.Order)
			                .ToList();
			if (!props.Any()) throw new InvalidOperationException($"No redis key for this model {type.Name}");
			object[] values = props.Select(x => x.Prop.GetValue(model))
			                       .ToArray();
			return GenerateKey(model.GetType(), values);
		}

		internal static string GenerateKey(Type type, params object[] keyValues)
		{
			string[] keyNames = type.GetProperties()
			                        .Where(x => x.IsDefined(typeof(RedisKeyAttribute)))
			                        .Select(x => new
			                        {
				                        Prop = x, x.GetCustomAttribute<RedisKeyAttribute>()
				                                   ?.Order
			                        })
			                        .OrderBy(x => x.Order)
			                        .Select(x => x.Prop.Name)
			                        .ToArray();

			if (keyNames.Length != keyValues.Length) throw new InvalidOperationException($"You should enter all keys for type {type.Name}");

			string[] keys = new string[keyNames.Length];
			for (int i = 0; i < keyNames.Length; i++)
				keys[i] = $"[{keyNames[i]}]_{keyValues[i]}";

			string key = string.Join('|', keys);
			return $"{type.FullName}_{key}";
		}

		private static CtorDelegate CreateConstructor(Type type)
		{
			// TODO : ILGenerator ile daha performanslı şekilde yazılacak. (GETTER'lar ayarlanacak get edildiğinde instance oluşacak)
			Type contextType = typeof(RedisContext);
			ConstructorInfo constructorInfo = type.GetConstructor(new[] {contextType});
			if (constructorInfo is null) return null;

			ParameterExpression parameter = Expression.Parameter(typeof(object[]));
			UnaryExpression ctorParameter = Expression.Convert(Expression.ArrayAccess(parameter, Expression.Constant(0)), contextType);
			NewExpression body = Expression.New(constructorInfo, ctorParameter);
			Expression<CtorDelegate> constructor = Expression.Lambda<CtorDelegate>(body, parameter);
			return constructor.Compile();
		}

		private void Connect()
		{
			RetryPolicy<bool> policy = Policy.HandleResult<bool>(connected => !connected)
			                                 .WaitAndRetry(10, r => TimeSpan.FromMilliseconds(100));

			ConfigurationOptions configure = Configure(_options.HostAndPort);

			PolicyResult<bool> result = policy.ExecuteAndCapture(() =>
			{
				try
				{
					if (_redis?.IsConnecting ?? false) return _redis.IsConnected;
					_redis = ConnectionMultiplexer.Connect(configure);
					Database = _redis.GetDatabase();
					return _redis.IsConnected;
				}
				catch
				{
					return false;
				}
			});

			if (result.Outcome == OutcomeType.Successful && !result.Result)
				throw new RedisConnectionException(ConnectionFailureType.InternalFailure, $"Redis connection is failed on ${_options.HostAndPort}");

			if (result.Outcome == OutcomeType.Failure)
				throw result.FinalException.InnerException ?? result.FinalException;
		}

		private void SetContext(bool buildIndex = false)
		{
			IEnumerable<PropertyInfo> props = GetType()
			                                  .GetProperties(BindingFlags.Instance | BindingFlags.Public)
			                                  .Where(x => x.PropertyType.IsClass &&
			                                              x.PropertyType.IsGenericType &&
			                                              x.PropertyType.GetGenericTypeDefinition() == typeof(Rediset<>));


			foreach (PropertyInfo prop in props)
			{
				CtorDelegate ctor = CreateConstructor(prop.PropertyType);
				Rediset instance = (Rediset) ctor(this);
				prop.SetValue(this, instance);
				if (buildIndex) instance.BuildIndex();
				_sets.Add(instance.EntityType, instance);
			}
		}

		private static ConfigurationOptions Configure(string hostAndPort)
		{
			ConfigurationOptions options = new ConfigurationOptions
			{
				EndPoints = {hostAndPort}
			};
			return options;
		}

		private void ReleaseUnmanagedResources()
		{
			_redis?.Close();
			_redis?.Dispose();
		}

		private void Dispose(bool disposing)
		{
			ReleaseUnmanagedResources();
			if (disposing) _redis?.Dispose();
		}

		~RedisContext()
		{
			Dispose(false);
		}

		private delegate object CtorDelegate(params object[] args);
	}
}