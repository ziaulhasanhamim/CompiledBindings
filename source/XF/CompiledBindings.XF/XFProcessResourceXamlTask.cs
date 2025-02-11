﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace CompiledBindings;

public class XFProcessResourceXamlTask : Task
{
	private readonly PlatformConstants _platformConstants;

	public XFProcessResourceXamlTask() : this(new PlatformConstants())
	{
	}

	public XFProcessResourceXamlTask(PlatformConstants platformConstants)
	{
		_platformConstants = platformConstants;
	}

	[Required]
	public ITaskItem[] ReferenceAssemblies { get; set; }

	[Required]
	public string Assembly { get; set; }

	public bool AttachDebugger { get; set; }

	public override bool Execute()
	{
		try
		{
			if (AttachDebugger)
			{
				System.Diagnostics.Debugger.Launch();
			}

			TypeInfoUtils.LoadReferences(ReferenceAssemblies.Select(a => a.ItemSpec));
			var prm = new ReaderParameters(ReadingMode.Immediate)
			{
				ReadWrite = true,
				ReadSymbols = true,
				AssemblyResolver = new TypeInfoUtils.AssemblyResolver(),
			};

			var xamlDomParser = new XFXamlDomParser(_platformConstants);

			var assembly = AssemblyDefinition.ReadAssembly(Assembly, prm);
			try
			{
				bool assemblyModified = false;
				foreach (var module in assembly.Modules)
				{
					List<(EmbeddedResource oldResource, EmbeddedResource newResource)> replaceResources = new();
					foreach (var resource in module.Resources.OfType<EmbeddedResource>())
					{
						if (!resource.Name.EndsWith(".xaml", StringComparison.InvariantCulture))
						{
							continue;
						}

						string xaml;
						using (var resourceStream = resource.GetResourceStream())
						using (var streamReader = new StreamReader(resourceStream))
						{
							xaml = streamReader.ReadToEnd();
						}

						XDocument xdoc;
						try
						{
							xdoc = XDocument.Parse(xaml, LoadOptions.SetLineInfo);
						}
						catch
						{
							continue;
						}

						var xclass = xdoc.Root.Attribute(xamlDomParser.xClass);
						if (xclass != null)
						{
							var mAttrs = xdoc.Descendants().SelectMany(e => e.Attributes()).Where(a => xamlDomParser.IsMemExtension(a)).ToList();
							var mxAttrs = xdoc.Descendants().SelectMany(e => e.Attributes()).Where(a => a.Name.Namespace == SimpleXamlDomParser.mxNamespace).ToList();
							if (mAttrs.Count > 0 || mxAttrs.Count > 0)
							{
								// Note! Here XAML is modified, but not with XDocument.
								// XDocument on save breaks original formatting completely.
								// It is important to keep original formatting, because there can
								// be errors in XAML.
								var lines = new List<TextLine>();
								var stringReader = new StringReader(xaml);
								string ln;
								while ((ln = stringReader.ReadLine()) != null)
								{
									lines.Add(new TextLine { Text = ln });
								}

								if (mAttrs.Count > 0)
								{
									var parts = xclass.Value.Split('.');
									string className = parts[parts.Length - 1];
									string classNs = string.Join(".", parts.Take(parts.Length - 1));

									var usedNames = new HashSet<string>(xdoc.Descendants().Select(e => e.Attribute(xamlDomParser.xName)).Where(a => a != null).Select(a => a.Value).Distinct());
									foreach (var xelement in xdoc.Descendants())
									{
										var xname = xelement.Attribute(xamlDomParser.xName);
										if (xname == null)
										{
											if (xelement != xdoc.Root &&
												(xelement.Attributes().Any(a => xamlDomParser.IsMemExtension(a)) ||
												 (xelement.Attribute(xamlDomParser.xDataType) != null && xelement.Name != xamlDomParser.DataTemplate)))
											{
												var name = XamlDomParser.GenerateName(xelement, usedNames);
												InsertAtEnd(xelement, $" x:Name=\"{name}\"");
											}
										}
										else
										{
											usedNames.Add(xname.Value);
										}
									}

									// Process DataTemplates
									var dataTemplates = xdoc.Descendants(xamlDomParser.DataTemplate)
										.Where(e => e.Descendants().SelectMany(e =>
											e.Attributes()).Any(a => xamlDomParser.IsMemExtension(a)))
										.ToList();
									if (dataTemplates.Count > 0)
									{
										string rootName;
										var xNameAttr = xdoc.Root.Attribute(xamlDomParser.xName);
										if (xNameAttr != null)
										{
											rootName = xNameAttr.Value;
										}
										else
										{
											rootName = XamlDomParser.GenerateName(xdoc.Root, usedNames);
											InsertAtEnd(xdoc.Root, $@" x:Name=""{rootName}""");
										}
										string? compiledBindingsNsPrefix = null, classNsPrefix = null;
										int dataTemplateIndex = 0, nsIndex = 0;
										foreach (var dataTemplate in dataTemplates)
										{
											var memExtensions = dataTemplate.Descendants()
												.SelectMany(e => e.Attributes())
												.Where(a =>
													xamlDomParser.IsMemExtension(a) &&
													EnumerableExtensions.SelectSequence(a.Parent, e => e.Parent, true).First(e => e.Name == xamlDomParser.DataTemplate) == dataTemplate)
												.ToList();
											if (memExtensions.Count == 0)
											{
												continue;
											}

											if (compiledBindingsNsPrefix == null)
											{
												compiledBindingsNsPrefix = SearchNsPrefix("CompiledBindings");
												classNsPrefix = SearchNsPrefix(classNs);

												string SearchNsPrefix(string clrNs)
												{
													var searchedUsingNs = "using:" + clrNs;
													var searchedClrNs = "clr-namespace:" + clrNs;
													string prefix;

													var attr = xdoc.Root.Attributes().FirstOrDefault(a =>
														a.Name.Namespace == XNamespace.Xmlns &&
														(a.Value == searchedUsingNs || a.Value == searchedClrNs));
													if (attr != null)
													{
														prefix = attr.Name.LocalName;
													}
													else
													{
														do
														{
															prefix = "g" + nsIndex++;
														}
														while (xdoc.Root.Attributes().Any(a =>
															a.Name.Namespace == XNamespace.Xmlns && a.Name.LocalName == prefix));

														InsertAtEnd(xdoc.Root, $" xmlns:{prefix}=\"using:{clrNs}\"");
													}

													return prefix;
												}
											}

											var regex = new Regex(@"{StaticResource\s+(\w+)}");

											var staticResources = memExtensions
												.SelectMany(a => regex.Matches(a.Value).Cast<Match>())
												.Select(m => m.Groups[1].Value)
												.Distinct();
											var propInitializers = string.Join(", ", staticResources.Select(r => $"{r}={{StaticResource {r}}}"));

											var rootElement = dataTemplate.Elements().First();
											InsertAtEnd(rootElement, $" {compiledBindingsNsPrefix}:DataTemplateBindings.Bindings=\"{{{classNsPrefix}:{className}_DataTemplate{dataTemplateIndex++} {propInitializers}}}\"");
										}
									}
								}

								// Remove attributes with CompiledBindings namespace
								foreach (var attr in mAttrs.Concat(mxAttrs))
								{
									var lineInfo = (IXmlLineInfo)attr;
									var lineNumber = lineInfo.LineNumber - 1;
									var textLine = lines[lineNumber];
									var start = lineInfo.LinePosition - 1 - textLine.RemovedTextOffset;
									Debug.Assert(start >= 0);
									var end = GetAttributeEnd(attr, textLine);
									Debug.Assert(end > start);
									var length = end - start;
									textLine.Text = textLine.Text.Remove(start, length);
									textLine.RemovedTextOffset += length;
								}

								// Write the changed text back to resources
								xaml = string.Join("\n", lines.Select(l => l.Text));
								var newResource = new EmbeddedResource(resource.Name, resource.Attributes, Encoding.UTF8.GetBytes(xaml));

								replaceResources.Add((resource, newResource));

								void InsertAtEnd(XElement xelement, string text)
								{
									var lineInfo = (IXmlLineInfo)xelement;

									int lineNumber, startPos;
									var attr = xelement.LastAttribute;
									if (attr == null)
									{
										lineNumber = lineInfo.LineNumber;
										startPos = lineInfo.LinePosition - 1 + xelement.Name.LocalName.Length;
									}
									else
									{
										var lineInfo2 = (IXmlLineInfo)attr;
										lineNumber = lineInfo2.LineNumber;
										startPos = GetAttributeEnd(attr, lines[lineNumber - 1]);
									}

									lineNumber--;
									var textLine = lines[lineNumber];
									var lineText = textLine.Text;
									var pos = lineText.IndexOf('>', startPos);
									if (pos == -1)
									{
										pos = lineText.Length;
									}
									else if (lineText[pos - 1] == '/')
									{
										pos--;
									}

									textLine.Text = lineText.Insert(pos, text);
								}

								int GetAttributeEnd(XAttribute attr, TextLine textLine)
								{
									var lineInfo = (IXmlLineInfo)attr;
									var start = lineInfo.LinePosition - 1 - textLine.RemovedTextOffset;
									Debug.Assert(start >= 0);
									var line = textLine.Text.Substring(start);
									var match = Regex.Match(line, "(.+?=\\s*?\".*?\").*");
									return start + match.Groups[1].Length;
								}
							}
						}
					}

					if (replaceResources.Count > 0)
					{
						foreach (var (oldResource, newResource) in replaceResources)
						{
							module.Resources.Remove(oldResource);
							module.Resources.Add(newResource);
						}
						assemblyModified |= true;
					}
				}

				if (assemblyModified)
				{
					assembly.Write(new WriterParameters { WriteSymbols = true });
				}
			}
			finally
			{
				assembly.Dispose();
			}

			return true;
		}
		catch (GeneratorException ex)
		{
			Log.LogError(null, null, null, ex.File, ex.LineNumber, ex.ColumnNumber, ex.EndLineNumber, ex.EndColumnNumber, ex.Message);
			return false;
		}
		catch (Exception ex)
		{
			Log.LogError(ex.Message);
			return false;
		}
		finally
		{
			TypeInfoUtils.Cleanup();
		}
	}

	private class TextLine
	{
		public string Text;
		public int RemovedTextOffset;
	}
}

