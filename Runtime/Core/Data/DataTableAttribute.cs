using System;

namespace Truesoft.Supabase.Core.Data
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class DataTableAttribute : Attribute
    {
        public string TableName { get; }

        /// <param name="tableName">테이블명. "data_" 접두사는 자동으로 붙으므로 생략합니다. 예: "inventory" → "data_inventory"</param>
        public DataTableAttribute(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("tableName must not be empty.", nameof(tableName));
            var name = tableName.Trim();
            TableName = name.StartsWith("data_", StringComparison.OrdinalIgnoreCase) ? name : "data_" + name;
        }
    }
}
