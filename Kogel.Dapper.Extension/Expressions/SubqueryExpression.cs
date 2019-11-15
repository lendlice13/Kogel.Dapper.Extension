﻿using Dapper;
using Kogel.Dapper.Extension.Exception;
using Kogel.Dapper.Extension.Extension;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System;
using Kogel.Dapper.Extension.Core.SetQ;
using System.Collections.ObjectModel;

namespace Kogel.Dapper.Extension.Expressions
{
	/// <summary>
	/// 专门处理子查询的的表达式树扩展类
	/// </summary>
	public class SubqueryExpression : ExpressionVisitor
	{
		private MethodCallExpression expression;
		private List<ParameterExpression> parameterExpressions;
		private readonly StringBuilder _sqlCmd;
		/// <summary>
		/// sql指令
		/// </summary>
		public string SqlCmd => _sqlCmd.ToString();
		/// <summary>
		/// 参数
		/// </summary>
		public DynamicParameters Param;
		/// <summary>
		/// 返回类型
		/// </summary>
		public Type ReturnType { get; set; }
		#region Kogel对象
		/// <summary>
		/// 查询对象
		/// </summary>
		public object QuerySet { get; set; }
		/// <summary>
		/// 排序对象
		/// </summary>
		public List<LambdaExpression> OrderBy { get; set; }
		/// <summary>
		/// 排序对象[倒叙]
		/// </summary>
		public List<LambdaExpression> OrderByDescing { get; set; }
		/// <summary>
		/// 条件表达式
		/// </summary>
		public List<LambdaExpression> WhereExpression { get; set; }
		#endregion

		public SubqueryExpression(MethodCallExpression methodCallExpression)
		{
			this.expression = methodCallExpression;
			this._sqlCmd = new StringBuilder();
			this.Param = new DynamicParameters();
			this.OrderBy = new List<LambdaExpression>();
			this.OrderByDescing = new List<LambdaExpression>();
			this.WhereExpression = new List<LambdaExpression>();
			this.AnalysisExpression();
		}
		/// <summary>
		/// 解析表达式
		/// </summary>
		public void AnalysisExpression()
		{
			//MethodCallExpression methodCall = (MethodCallExpression)(expression.Object);
			////获取queryset对象
			//var querySet = methodCall.Object.ToConvertAndGetValue();
			////获取paramerer对象
			//foreach (UnaryExpression exp in methodCall.Arguments)
			//{
			//	this.parameterExpressions = new List<ParameterExpression>();
			//	Visit(exp);
			//	var lambda = Expression.Lambda(exp, parameterExpressions.ToList());
			//	WhereExpression.Add(lambda);
			//}
			AnalysisKogelExpression(expression);

			//动态执行，得到T类型
			typeof(SubqueryExpression)
						.GetMethod("FormatSend")
						.MakeGenericMethod(QuerySet.GetType().GenericTypeArguments[0])
						.Invoke(this, new object[] { QuerySet, this.expression.Method.Name });
		}
		/// <summary>
		/// 递归解析导航查询表达式
		/// </summary>
		/// <param name="methodCallExpression"></param>
		/// <returns></returns>
		public void AnalysisKogelExpression(MethodCallExpression methodCallExpression)
		{
			switch (methodCallExpression.Method.Name)
			{
				case "QuerySet":
					{
						if (this.QuerySet == null)
							this.QuerySet = methodCallExpression.ToConvertAndGetValue();
						break;
					}
				case "Join":
					{
						this.QuerySet = methodCallExpression.ToConvertAndGetValue();
						//methodCallExpression.Method.Invoke(this.QuerySet, methodCallExpression.Arguments.Select(x => x.ToConvertAndGetValue()).ToArray());
						break;
					}
				case "Where":
					{
						foreach (UnaryExpression exp in methodCallExpression.Arguments)
						{
							this.parameterExpressions = new List<ParameterExpression>();
							Visit(exp);
							var lambda = Expression.Lambda(exp, parameterExpressions.ToList());
							this.WhereExpression.Add(lambda);
						}
						break;
					}
				case "OrderBy":
					{
						foreach (UnaryExpression exp in methodCallExpression.Arguments)
						{
							var lambda = exp.GetLambdaExpression();
							this.OrderBy.Add(lambda);
						}
						break;
					}
				case "OrderByDescing":
					{
						foreach (UnaryExpression exp in methodCallExpression.Arguments)
						{
							var lambda = exp.GetLambdaExpression();
							this.OrderByDescing.Add(lambda);
						}
						break;
					}
			}
			if (methodCallExpression.Object != null)
			{
				if (methodCallExpression.Object is MethodCallExpression)
				{
					var objectCallExpression = methodCallExpression.Object as MethodCallExpression;
					AnalysisKogelExpression(objectCallExpression);
				}
			}
		}
		/// <summary>
		/// 解析参数
		/// </summary>
		/// <param name="node"></param>
		/// <returns></returns>
		protected override Expression VisitParameter(ParameterExpression node)
		{
			if (!parameterExpressions.Exists(x => x.Name == node.Name))
			{
				ParameterExpression param = Expression.Parameter(node.Type, node.Name);
				parameterExpressions.Add(param);
			}
			return node;
		}
		/// <summary>
		/// 替换成新的参数名，防止命名冲突
		/// </summary>
		/// <param name="param"></param>
		/// <param name="sql"></param>
		/// <returns></returns>
		private DynamicParameters ToSubqueryParam(DynamicParameters param, ref string sql)
		{
			DynamicParameters newParam = new DynamicParameters();
			foreach (var paramName in param.ParameterNames)
			{
				string newName = paramName + "_Subquery";
				object value = param.Get<object>(paramName);
				newParam.Add(newName, value);
				sql = sql.Replace(paramName, newName);
			}
			return newParam;
		}
		/// <summary>
		/// 反射执行需要指向T类型的函数
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="sqlProvider"></param>
		/// <param name="methodName"></param>
		public void FormatSend<T>(QuerySet<T> querySet, string methodName)
		{
			SqlProvider sqlProvider = querySet.SqlProvider;
			//写入重新生成后的条件
			if (WhereExpression != null && WhereExpression.Any())
			{
				querySet.WhereExpressionList.AddRange(WhereExpression);
			}
			//写入排序
			if (OrderBy != null && OrderBy.Any())
			{
				foreach (LambdaExpression exp in OrderBy)
					querySet.OrderbyExpressionList.Add(exp, Model.EOrderBy.Asc);
			}
			//写入倒序
			if (OrderByDescing != null && OrderByDescing.Any())
			{
				foreach (LambdaExpression exp in OrderByDescing)
					querySet.OrderbyExpressionList.Add(exp, Model.EOrderBy.Asc);
			}
			switch (methodName)
			{
				case "Count":
					{
						sqlProvider.FormatCount();
					}
					break;
				case "Sum":
					{
						var lambda = this.expression.Arguments[0].GetLambdaExpression();
						sqlProvider.FormatSum(lambda);

					}
					break;
				case "Min":
					{
						var lambda = this.expression.Arguments[0].GetLambdaExpression();
						sqlProvider.FormatMin(lambda);

					}
					break;
				case "Max":
					{
						var lambda = this.expression.Arguments[0].GetLambdaExpression();
						sqlProvider.FormatMax(lambda);

					}
					break;
				case "Get":
					{
						//加上自定义实体返回
						var lambda = this.expression.Arguments[0].GetLambdaExpression();
						this.ReturnType = lambda.ReturnType;
						sqlProvider.Context.Set.SelectExpression = lambda;
						sqlProvider.FormatGet<T>();
					}
					break;
				case "ToList":
					{
						//加上自定义实体返回
						var lambda = this.expression.Arguments[0].GetLambdaExpression();
						this.ReturnType = lambda.ReturnType;
						sqlProvider.Context.Set.SelectExpression = lambda;
						sqlProvider.FormatToList<T>();
					}
					break;
				default:
					throw new DapperExtensionException("the expression is no support this function");
			}
			//得到解析的sql和param对象
			string sql = sqlProvider.SqlString;
			var param = ToSubqueryParam(sqlProvider.Params, ref sql);
			_sqlCmd.Append(sql);
			this.Param.AddDynamicParams(param);
		}
	}
}
