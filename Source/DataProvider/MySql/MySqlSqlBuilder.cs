﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LinqToDB.DataProvider.MySql
{
	using SqlQuery;
	using SqlProvider;

	public class MySqlSqlBuilder : BasicSqlBuilder
	{
		public MySqlSqlBuilder(ISqlOptimizer sqlOptimizer, SqlProviderFlags sqlProviderFlags)
			: base(sqlOptimizer, sqlProviderFlags)
		{
		}

		static MySqlSqlBuilder()
		{
			ParameterSymbol = '@';
		}

		public override int CommandCount(SelectQuery selectQuery)
		{
			return selectQuery.IsInsert && selectQuery.Insert.WithIdentity ? 2 : 1;
		}

		protected override void BuildCommand(int commandNumber, StringBuilder sb)
		{
			sb.AppendLine("SELECT LAST_INSERT_ID()");
		}

		protected override ISqlBuilder CreateSqlProvider()
		{
			return new MySqlSqlBuilder(SqlOptimizer, SqlProviderFlags);
		}

		protected override string LimitFormat { get { return "LIMIT {0}"; } }

		public override bool IsNestedJoinParenthesisRequired { get { return true; } }

		protected override void BuildOffsetLimit(StringBuilder sb)
		{
			if (SelectQuery.Select.SkipValue == null)
				base.BuildOffsetLimit(sb);
			else
			{
				AppendIndent(sb)
					.AppendFormat(
						"LIMIT {0},{1}",
						BuildExpression(new StringBuilder(), SelectQuery.Select.SkipValue),
						SelectQuery.Select.TakeValue == null ?
							long.MaxValue.ToString() :
							BuildExpression(new StringBuilder(), SelectQuery.Select.TakeValue).ToString())
					.AppendLine();
			}
		}

		protected override void BuildDataType(StringBuilder sb, SqlDataType type, bool createDbType = false)
		{
			switch (type.DataType)
			{
				case DataType.Int32         :
				case DataType.UInt16        :
				case DataType.Int16         :
					if (createDbType) goto default;
					sb.Append("Signed");
					break;
				case DataType.SByte         :
				case DataType.Byte          :
					if (createDbType) goto default;
					sb.Append("Unsigned");
					break;
				case DataType.Money         : sb.Append("Decimal(19,4)"); break;
				case DataType.SmallMoney    : sb.Append("Decimal(10,4)"); break;
#if !MONO
				case DataType.DateTime2     :
#endif
				case DataType.SmallDateTime : sb.Append("DateTime");      break;
				case DataType.Boolean       : sb.Append("Boolean");       break;
				case DataType.Double        :
				case DataType.Single        : base.BuildDataType(sb, SqlDataType.Decimal); break;
				case DataType.VarChar       :
				case DataType.NVarChar      :
					sb.Append("Char");
					if (type.Length > 0)
						sb.Append('(').Append(type.Length).Append(')');
					break;
				default: base.BuildDataType(sb, type); break;
			}
		}

		protected override void BuildDeleteClause(StringBuilder sb)
		{
			var table = SelectQuery.Delete.Table != null ?
				(SelectQuery.From.FindTableSource(SelectQuery.Delete.Table) ?? SelectQuery.Delete.Table) :
				SelectQuery.From.Tables[0];

			AppendIndent(sb)
				.Append("DELETE ")
				.Append(Convert(GetTableAlias(table), ConvertType.NameToQueryTableAlias))
				.AppendLine();
		}

		protected override void BuildUpdateClause(StringBuilder sb)
		{
			base.BuildFromClause(sb);
			sb.Remove(0, 4).Insert(0, "UPDATE");
			base.BuildUpdateSet(sb);
		}

		protected override void BuildFromClause(StringBuilder sb)
		{
			if (!SelectQuery.IsUpdate)
				base.BuildFromClause(sb);
		}

		public static char ParameterSymbol           { get; set; }
		public static bool TryConvertParameterSymbol { get; set; }

		private static string _commandParameterPrefix = "";
		public  static string  CommandParameterPrefix
		{
			get { return _commandParameterPrefix; }
			set { _commandParameterPrefix = string.IsNullOrEmpty(value) ? string.Empty : value; }
		}

		private static string _sprocParameterPrefix = "";
		public  static string  SprocParameterPrefix
		{
			get { return _sprocParameterPrefix; }
			set { _sprocParameterPrefix = string.IsNullOrEmpty(value) ? string.Empty : value; }
		}

		private static List<char> _convertParameterSymbols;
		public  static List<char>  ConvertParameterSymbols
		{
			get { return _convertParameterSymbols; }
			set { _convertParameterSymbols = value ?? new List<char>(); }
		}

		public override object Convert(object value, ConvertType convertType)
		{
			switch (convertType)
			{
				case ConvertType.NameToQueryParameter:
					return ParameterSymbol + value.ToString();

				case ConvertType.NameToCommandParameter:
					return ParameterSymbol + CommandParameterPrefix + value;

				case ConvertType.NameToSprocParameter:
					{
						var valueStr = value.ToString();

						if(string.IsNullOrEmpty(valueStr))
							throw new ArgumentException("Argument 'value' must represent parameter name.");

						if (valueStr[0] == ParameterSymbol)
							valueStr = valueStr.Substring(1);

						if (valueStr.StartsWith(SprocParameterPrefix, StringComparison.Ordinal))
							valueStr = valueStr.Substring(SprocParameterPrefix.Length);

						return ParameterSymbol + SprocParameterPrefix + valueStr;
					}

				case ConvertType.SprocParameterToName:
					{
						var str = value.ToString();
						str = (str.Length > 0 && (str[0] == ParameterSymbol || (TryConvertParameterSymbol && ConvertParameterSymbols.Contains(str[0])))) ? str.Substring(1) : str;

						if (!string.IsNullOrEmpty(SprocParameterPrefix) && str.StartsWith(SprocParameterPrefix))
							str = str.Substring(SprocParameterPrefix.Length);

						return str;
					}

				case ConvertType.NameToQueryField     :
				case ConvertType.NameToQueryFieldAlias:
				case ConvertType.NameToQueryTableAlias:
					{
						var name = value.ToString();
						if (name.Length > 0 && name[0] == '`')
							return value;
						return "`" + value + "`";
					}

				case ConvertType.NameToDatabase   :
				case ConvertType.NameToOwner      :
				case ConvertType.NameToQueryTable :
					if (value != null)
					{
						var name = value.ToString();
						if (name.Length > 0 && name[0] == '`')
							return value;

						if (name.IndexOf('.') > 0)
							value = string.Join("`.`", name.Split('.'));

						return "`" + value + "`";
					}

					break;
			}

			return value;
		}

		protected override StringBuilder BuildExpression(StringBuilder sb, ISqlExpression expr, bool buildTableName, bool checkParentheses, string alias, ref bool addAlias)
		{
			return base.BuildExpression(
				sb,
				expr,
				buildTableName && SelectQuery.QueryType != QueryType.InsertOrUpdate,
				checkParentheses,
				alias,
				ref addAlias);
		}

		protected override void BuildInsertOrUpdateQuery(StringBuilder sb)
		{
			BuildInsertQuery(sb);
			AppendIndent(sb).AppendLine("ON DUPLICATE KEY UPDATE");

			Indent++;

			var first = true;

			foreach (var expr in SelectQuery.Update.Items)
			{
				if (!first)
					sb.Append(',').AppendLine();
				first = false;

				AppendIndent(sb);
				BuildExpression(sb, expr.Column, false, true);
				sb.Append(" = ");
				BuildExpression(sb, expr.Expression, false, true);
			}

			Indent--;

			sb.AppendLine();
		}

		protected override void BuildEmptyInsert(StringBuilder sb)
		{
			sb.AppendLine("() VALUES ()");
		}

		protected override void BuildCreateTableIdentityAttribute1(StringBuilder sb, SqlField field)
		{
			sb.Append("AUTO_INCREMENT");
		}

		protected override void BuildCreateTablePrimaryKey(StringBuilder sb, string pkName, IEnumerable<string> fieldNames)
		{
			sb.Append("CONSTRAINT ").Append(pkName).Append(" PRIMARY KEY CLUSTERED (");
			sb.Append(fieldNames.Aggregate((f1,f2) => f1 + ", " + f2));
			sb.Append(")");
		}

		protected override void BuildString(StringBuilder sb, string value)
		{
			base.BuildString(sb, value.Replace("\\", "\\\\"));
		}

		protected override void BuildChar(StringBuilder sb, char value)
		{
			if (value == '\\')
				sb.Append("\\\\");
			else
				base.BuildChar(sb, value);
		}
	}
}