using System.Linq.Expressions;

namespace RedisworkCore.Redisearch
{
	public enum RedisearchNodeType
	{
		StartsWith,
		EndsWith,
		Contains,
		AndAlso = ExpressionType.AndAlso,
		OrElse = ExpressionType.OrElse,
		Equal = ExpressionType.Equal,
		NotEqual = ExpressionType.NotEqual,
		GreaterThan = ExpressionType.GreaterThan,
		GreaterThanOrEqual = ExpressionType.GreaterThanOrEqual,
		LessThan = ExpressionType.LessThan,
		LessThanOrEqual = ExpressionType.LessThanOrEqual
	}
}