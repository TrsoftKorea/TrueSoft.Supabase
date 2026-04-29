using System;

namespace Truesoft.Supabase.Core.Data
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class DataTableAttribute : Attribute
    {
        public string TableName { get; }

        public DataTableAttribute(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("tableName must not be empty.", nameof(tableName));
            TableName = tableName.Trim();
        }
    }
}
