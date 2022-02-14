using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlogStudio
{
	public class IndexableExpandoObject : DynamicObject
	{
		// The inner dictionary to store field names and values.
		Dictionary<string, object?> dictionary
			= new Dictionary<string, object?>();

		// Get the property value.
		public override bool TryGetMember(
			GetMemberBinder binder, out object? result)
		{
			_ = dictionary.TryGetValue(binder.Name, out result);
			return true;
		}

		// Set the property value.
		public override bool TrySetMember(
			SetMemberBinder binder, object? value)
		{
			dictionary[binder.Name] = value;
			return true;
		}

		// Set the property value by index.
		public override bool TrySetIndex(
			SetIndexBinder binder, object[] indexes, object? value)
		{
			if (indexes.Length != 1 || indexes[0] is not string index) throw new ArgumentException("indexes");
			dictionary[index] = value;
			return true;
		}

		// Get the property value by index.
		public override bool TryGetIndex(
			GetIndexBinder binder, object[] indexes, out object? result)
		{
			if (indexes.Length != 1 || indexes[0] is not string index) throw new ArgumentException("indexes");
			_ = dictionary.TryGetValue(index, out result);
			return true;
		}
	}
}
