﻿using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

#nullable enable

namespace CompiledBindings
{
	public class XamlObject
	{
		public XamlObject(XamlNode xamlNode, TypeInfo type)
		{
			XamlNode = xamlNode;
			Type = type;
		}

		public XamlNode XamlNode { get; }
		public TypeInfo Type { get; }

		public string? Name { get; set; }
		public bool NameExplicitlySet { get; set; }
		public XamlObject? Parent { get; set; }
		public bool IsRoot { get; set; }

		public List<XamlObjectProperty> Properties { get; } = new List<XamlObjectProperty>();

		public string? CreateCSharpValue { get; set; }

		public bool GenerateMember { get; set; }
		public string? FieldModifier { get; set; }

		public IEnumerable<Bind> EnumerateBinds()
		{
			return Properties.Select(p => p.Value.BindValue).Where(b => b != null)!;
		}
	}

	public class XamlObjectProperty
	{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
		public XamlNode XamlNode { get; set; }
		public XamlObject Object { get; set; }
		public string MemberName { get; set; }
		public XamlObjectValue Value { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

		public bool IsAttached;

		public PropertyInfo? TargetProperty { get; set; }
		public MethodInfo? TargetMethod { get; set; }
		public EventDefinition? TargetEvent { get; set; }

		public TypeInfo MemberType => TargetMethod?.Parameters.Last().ParameterType ?? TargetProperty?.PropertyType ?? (TypeInfo)TargetEvent!.EventType;

		public HashSet<string> IncludeNamespaces { get; } = new HashSet<string>();
	}

	public class XamlObjectValue
	{
		public XamlObjectValue(XamlObjectProperty property)
		{
			Property = property;
		}
		public XamlObjectProperty Property { get; }

		public XamlObject? ObjectValue { get; set; }
		public List<XamlObject>? CollectionValue { get; set; }
		public Expression? StaticValue { get; set; }
		public Bind? BindValue { get; set; }
		public string? CSharpValue { get; set; }
	}

	public class ResourceDictionary
	{
		public string? Source { get; set; }
		public List<ResourceDictionary> MergedDictionaries { get; } = new List<ResourceDictionary>();
		public List<StaticResource> Values { get; } = new List<StaticResource>();
		public List<Style> Styles { get; } = new List<Style>();
	}

	public class Style
	{
		public Style(string? key, TypeInfo targetType)
		{
			Key = key;
			TargetType = targetType;
		}

		public string? Key { get; set; }
		public TypeInfo TargetType { get; }

		public Style? BasedOn { get; set; }
		public List<XamlNode> Setters { get; } = new List<XamlNode>();
	}

	public class StaticResource
	{
		public StaticResource(string? key, XamlObject obj)
		{
			Key = key;
			Object = obj;
		}
		public string? Key { get; }
		public XamlObject Object { get; }
		public bool IsUsed { get; set; }
	}

}