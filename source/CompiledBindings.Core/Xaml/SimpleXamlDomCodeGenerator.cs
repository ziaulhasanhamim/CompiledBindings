﻿namespace CompiledBindings;

public abstract class SimpleXamlDomCodeGenerator : XamlCodeGenerator
{
	private readonly BindingsCodeGenerator _bindingsCodeGenerator;
	private readonly string _bindingContextStart;
	private readonly string _bindingConextArgs;
	private readonly string _bindableObject;
	private readonly string _findByNameFormat;
	private readonly bool _generateVariableDeclarations;
	private readonly bool _generateVariableInitialization;
	private bool _asyncFunctions;

	public SimpleXamlDomCodeGenerator(BindingsCodeGenerator bindingsCodeGenerator,
									  string bindingContextStart,
									  string bindingConextArgs,
									  string bindableObject,
									  string findByNameFormat,
									  bool generateVariableDeclarations,
									  bool generateVariableInitialization,
									  string langVersion,
									  string msbuildVersion)
		: base(langVersion, msbuildVersion)
	{
		_bindingsCodeGenerator = bindingsCodeGenerator;
		_bindingContextStart = bindingContextStart;
		_bindingConextArgs = bindingConextArgs;
		_bindableObject = bindableObject;
		_findByNameFormat = findByNameFormat;
		_generateVariableDeclarations = generateVariableDeclarations;
		_generateVariableInitialization = generateVariableInitialization;
	}

	public string GenerateCode(SimpleXamlDom parseResult)
	{
		var output = new StringBuilder();

		var taskType = TypeInfo.GetTypeThrow(typeof(System.Threading.Tasks.Task));
		_asyncFunctions = parseResult.UpdateMethod.SetExpressions.Any(e => taskType.IsAssignableFrom(e.Expression.Type));

		if (parseResult.TargetType!.Reference.Namespace != null)
		{
			output.AppendLine(
$@"namespace {parseResult.TargetType.Reference.Namespace}
{{");
		}

		if (parseResult.IncludeNamespaces.Count > 0)
		{
			foreach (string ns in parseResult.IncludeNamespaces)
			{
				output.AppendLine(
$@"	using {ns};");
			}
			output.AppendLine();
		}

		if (LangNullables)
		{
			output.AppendLine(
$@"#nullable disable");
		}

		output.AppendLine(
$@"
	[System.CodeDom.Compiler.GeneratedCode(""CompiledBindings"", null)]
	partial class {parseResult.TargetType.Reference.Name}
	{{");
		if (_asyncFunctions)
		{
			output.AppendLine(
$@"		global::System.Threading.CancellationTokenSource _generatedCodeDisposed = new global::System.Threading.CancellationTokenSource();");
		}

		if (parseResult.GenerateInitializeMethod)
		{
			GenerateInitializeMethod(output, parseResult);
		}
		GenerateBindings(output, parseResult, parseResult.TargetType.Reference.Name);

		output.AppendLine(
$@"	}}");

		GenerateDataTemplates(output, parseResult, parseResult.TargetType.Reference.Name);

		if (parseResult.TargetType.Reference.Namespace != null)
		{
			output.AppendLine(
"}");
		}

		return output.ToString();
	}

	protected virtual string IGeneratedDataTemplateFullName => "CompiledBindings.IGeneratedDataTemplate";

	private void GenerateResourceDeclarations(StringBuilder output, SimpleXamlDom parseResult, bool isDataTemplate)
	{
		var resources = parseResult.XamlObjects.SelectMany(o => o.Properties).Select(p => p.Value.BindValue).Where(b => b != null).SelectMany(b => b!.Resources).Distinct(b => b.name);
		foreach (var (name, type) in resources)
		{
			if (isDataTemplate)
			{
				output.AppendLine(
$@"		public global::{type.Reference.GetCSharpFullName()} {name} {{ get; set; }}");
			}
			else
			{
				output.AppendLine(
$@"		global::{type.Reference.GetCSharpFullName()} {name};");
			}
		}
	}

	private void GenerateInitializeResources(StringBuilder output, SimpleXamlDom parseResult)
	{
		var resources = parseResult.XamlObjects.SelectMany(o => o.Properties).Select(p => p.Value.BindValue).Where(b => b != null).SelectMany(b => b!.Resources).Distinct(b => b.name).ToList();
		if (resources.Count > 0)
		{
			foreach (var (name, type) in resources)
			{
				output.AppendLine(
$@"			{name} = (global::{type.Reference.GetCSharpFullName()})({CreateGetResourceCode(name)});");
			}

			output.AppendLine();
		}
	}

	protected abstract string CreateGetResourceCode(string resourceName);

	private void GenerateInitializeMethod(StringBuilder output, SimpleXamlDom parseResult)
	{
		if (_generateVariableDeclarations)
		{
			GenerateVariablesDeclarations(output, parseResult, true);
		}
		GenerateResourceDeclarations(output, parseResult, false);

		output.AppendLine(
$@"		private bool _generatedCodeInitialized;

		private void InitializeAfterConstructor()
		{{
			if (_generatedCodeInitialized)
				return;

			_generatedCodeInitialized = true;
");

		GenerateInitializeMethodBody(output, parseResult, "this", false);

		output.AppendLine(
$@"		}}");

		GenerateDestructorMethod(output, parseResult, "this");

		GenerateBindingContextChangedHandlers(output, parseResult);
	}

	private void GenerateDestructorMethod(StringBuilder output, SimpleXamlDom parseResult, string rootElement)
	{
		if (parseResult.BindingScopes.Count > 0)
		{
			output.AppendLine();

			if (parseResult.HasDestructor)
			{
				output.AppendLine(
$@"		private void DeinitializeAfterDestructor()");
			}
			else
			{
				output.AppendLine(
$@"		~{parseResult.TargetType!.Reference.Name}()");
			}
			output.AppendLine(
$@"		{{");
			if (_asyncFunctions)
			{
				output.AppendLine(
$@"			_generatedCodeDisposed.Cancel();");
			}

			foreach (var bs in parseResult.BindingScopes)
			{
				var viewName = "_";
				if (bs.DataType != null)
				{
					viewName += bs.ViewName ?? "this";
				}
				output.AppendLine(
$@"			if (Bindings{viewName} != null)
			{{
				Bindings{viewName}.Cleanup();
			}}");
			}

			output.AppendLine(
$@"		}}");
		}
	}

	private void GenerateBindingContextChangedHandlers(StringBuilder output, SimpleXamlDom parseResult)
	{
		foreach (var bs in parseResult.BindingScopes.Where(b => b.DataType != null))
		{
			var viewName = bs.ViewName ?? "this";
			var prm = bs.DataType!.Reference.FullName == parseResult.TargetType?.Reference.FullName ? null : $", dataRoot";

			output.AppendLine(
$@"
		private void {bs.ViewName ?? "this"}_{_bindingContextStart}ContextChanged(object sender, global::{_bindingConextArgs} e)
		{{
			Bindings_{viewName}.Cleanup();");

			string? a = null;
			if (bs.DataType != null)
			{
				output.AppendLine(
$@"			if (((global::{_bindableObject})sender).{_bindingContextStart}Context is global::{bs.DataType.Reference.GetCSharpFullName()} dataRoot)
			{{");
				a = "\t";
			}
			output.AppendLine(
$@"{a}			Bindings_{viewName}.Initialize(this{prm});");
			if (bs.DataType != null)
			{
				output.AppendLine(
$@"			}}");
			}

			output.AppendLine(
$@"		}}");
		}

	}

	private void GenerateInitializeMethodBody(StringBuilder output, SimpleXamlDom parseResult, string rootElement, bool isDataTemplate)
	{
		if (isDataTemplate || _generateVariableInitialization)
		{
			IEnumerable<XamlObject> objects = parseResult.XamlObjects!;
			if (!isDataTemplate)
			{
				objects = objects.Where(o => !o.NameExplicitlySet);
			}

			foreach (var obj in objects.Where(o => o.Name != null))
			{
				output.AppendLine(
$@"			{obj.Name} = {string.Format(_findByNameFormat, obj.Type.Reference.GetCSharpFullName(), rootElement, obj.Name)};");
			}
			output.AppendLine();
		}

		if (!isDataTemplate)
		{
			GenerateInitializeResources(output, parseResult);
		}

		_bindingsCodeGenerator.GenerateSetExpressions(output, parseResult.UpdateMethod);
		output.AppendLine();

		for (int i = 0; i < parseResult.BindingScopes.Count; i++)
		{
			var bs = parseResult.BindingScopes[i];
			var viewName = bs.ViewName ?? rootElement;
			if (bs.DataType == null)
			{
				output.AppendLine(
$@"			Bindings_.Initialize(this);");
			}
			else
			{
				var prm = bs.DataType.Reference.FullName == parseResult.TargetType?.Reference.FullName ? null : $", dataRoot" + i;
				output.AppendLine(
$@"			{viewName}.{_bindingContextStart}ContextChanged += {viewName}_{_bindingContextStart}ContextChanged;
			if ({viewName}.{_bindingContextStart}Context is global::{bs.DataType.Reference.GetCSharpFullName()} dataRoot{i})
			{{
				Bindings_{viewName}.Initialize(this{prm});
			}}");
			}
		}
	}

	private void GenerateBindings(StringBuilder output, SimpleXamlDom parseResult, string className)
	{
		foreach (var bs in parseResult.BindingScopes)
		{
			output.AppendLine();
			var nameSuffix = "_";
			if (bs.DataType != null)
			{
				nameSuffix += bs.ViewName ?? "this";
			}
			_bindingsCodeGenerator.GenerateBindingsClass(output, bs.BindingsData!, className, nameSuffix: nameSuffix);
		}
	}

	private void GenerateDataTemplates(StringBuilder output, SimpleXamlDom parseResult, string classBaseName)
	{
		for (int i = 0; i < parseResult.DataTemplates.Count; i++)
		{
			GenerateDataTemplateClass(output, parseResult.DataTemplates[i], classBaseName + "_DataTemplate" + i);
		}
	}

	private void GenerateDataTemplateClass(StringBuilder output, SimpleXamlDom parseResult, string dataTemplateClassName)
	{
		parseResult.BindingScopes.Where(bs => bs.ViewName == null).ForEach(bs => bs.ViewName = "rootElement");

		output.AppendLine(
$@"
	class {dataTemplateClassName} : global::{IGeneratedDataTemplateFullName}
	{{");

		GenerateVariablesDeclarations(output, parseResult, false);
		GenerateResourceDeclarations(output, parseResult, true);

		// Initialize method
		output.AppendLine(
$@"
		public void Initialize(global::{_bindableObject} rootElement)
		{{");

		GenerateInitializeMethodBody(output, parseResult, "rootElement", true);

		output.AppendLine(
$@"		}}");

		// Cleanup method
		output.AppendLine();
		output.AppendLine(
$@"		public void Cleanup(global::{_bindableObject} rootElement)
		{{");

		foreach (var bs in parseResult.BindingScopes)
		{
			if (bs.DataType == null)
			{
				output.AppendLine(
$@"			Bindings.Cleanup();");
			}
			else
			{
				var viewName = bs.ViewName ?? "rootElement";
				output.AppendLine(
$@"			{viewName}.{_bindingContextStart}ContextChanged -= {viewName}_{_bindingContextStart}ContextChanged;
			Bindings_{viewName}.Cleanup();");
			}
		}

		output.AppendLine(
$@"		}}");


		GenerateBindingContextChangedHandlers(output, parseResult);

		GenerateBindings(output, parseResult, dataTemplateClassName);

		output.AppendLine(
$@"	}}");
	}

	private void GenerateVariablesDeclarations(StringBuilder output, SimpleXamlDom parseResult, bool notExplicitlySet)
	{
		IEnumerable<XamlObject> objects = parseResult.XamlObjects!;
		if (notExplicitlySet)
		{
			objects = objects.Where(o => !o.NameExplicitlySet);
		}
		foreach (var obj in objects.Where(o => o.Name != null))
		{
			output.AppendLine(
$@"		private global::{obj.Type.Reference.GetCSharpFullName()} {obj.Name};");
		}
	}
}

