﻿using System;
using System.Collections.Immutable;
using System.Linq;
using Meziantou.Analyzer.Internals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Analyzer.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OptimizeStartsWithAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifiers.OptimizeStartsWith,
        title: "Optimize string method usage",
        messageFormat: "Use an overload with char instead of string",
        RuleCategories.Performance,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "",
        helpLinkUri: RuleIdentifiers.GetHelpUri(RuleIdentifiers.OptimizeStartsWith));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(ctx =>
        {
            var analyzerContext = new AnalyzerContext(ctx.Compilation);
            ctx.RegisterOperationAction(analyzerContext.AnalyzeInvocation, OperationKind.Invocation);
        });
    }

    private sealed class AnalyzerContext
    {
        public AnalyzerContext(Compilation compilation)
        {
            StringComparisonSymbol = compilation.GetBestTypeByMetadataName("System.StringComparison");
            if (StringComparisonSymbol is not null)
            {
                StringComparison_Ordinal = StringComparisonSymbol.GetMembers(nameof(StringComparison.Ordinal)).FirstOrDefault();
                StringComparison_CurrentCulture = StringComparisonSymbol.GetMembers(nameof(StringComparison.CurrentCulture)).FirstOrDefault();
#pragma warning disable RS0030 // Do not use banned APIs
                StringComparison_InvariantCulture = StringComparisonSymbol.GetMembers(nameof(StringComparison.InvariantCulture)).FirstOrDefault();
#pragma warning restore RS0030
            }

            EnumerableOfTSymbol = compilation.GetBestTypeByMetadataName("System.Collections.Generic.IEnumerable`1");

            var stringSymbol = compilation.GetSpecialType(SpecialType.System_String);
            if (stringSymbol is not null)
            {
                foreach (var method in stringSymbol.GetMembers(nameof(string.StartsWith)).OfType<IMethodSymbol>())
                {
                    if (!method.IsStatic && method.Parameters.Length == 1 && method.Parameters[0].Type.IsChar())
                    {
                        StartsWith_Char = method;
                        break;
                    }
                }

                foreach (var method in stringSymbol.GetMembers(nameof(string.EndsWith)).OfType<IMethodSymbol>())
                {
                    if (!method.IsStatic && method.Parameters.Length == 1 && method.Parameters[0].Type.IsChar())
                    {
                        EndsWith_Char = method;
                        break;
                    }
                }

                foreach (var method in stringSymbol.GetMembers(nameof(string.Replace)).OfType<IMethodSymbol>())
                {
                    if (!method.IsStatic && method.Parameters.Length == 2 && method.Parameters[0].Type.IsChar() && method.Parameters[1].Type.IsChar())
                    {
                        Replace_Char_Char = method;
                        break;
                    }
                }

                foreach (var method in stringSymbol.GetMembers(nameof(string.IndexOf)).OfType<IMethodSymbol>())
                {
                    if (method.IsStatic)
                        continue;

                    if (method.Parameters.Length == 1 && method.Parameters[0].Type.IsChar())
                    {
                        IndexOf_Char = method;
                    }
                    else if (method.Parameters.Length == 2 && method.Parameters[0].Type.IsChar() && method.Parameters[1].Type.IsInt32())
                    {
                        IndexOf_Char_Int32 = method;
                    }
                    else if (method.Parameters.Length == 3 && method.Parameters[0].Type.IsChar() && method.Parameters[1].Type.IsInt32() && method.Parameters[2].Type.IsInt32())
                    {
                        IndexOf_Char_Int32_Int32 = method;
                    }
                    else if (method.Parameters.Length == 2 && method.Parameters[0].Type.IsChar() && method.Parameters[1].Type.IsEqualTo(StringComparisonSymbol))
                    {
                        IndexOf_Char_StringComparison = method;
                    }
                }

                foreach (var method in stringSymbol.GetMembers(nameof(string.LastIndexOf)).OfType<IMethodSymbol>())
                {
                    if (method.IsStatic)
                        continue;

                    if (method.Parameters.Length == 1 && method.Parameters[0].Type.IsChar())
                    {
                        LastIndexOf_Char = method;
                    }
                    else if (method.Parameters.Length == 2 && method.Parameters[0].Type.IsChar() && method.Parameters[1].Type.IsInt32())
                    {
                        LastIndexOf_Char_Int32 = method;
                    }
                    else if (method.Parameters.Length == 3 && method.Parameters[0].Type.IsChar() && method.Parameters[1].Type.IsInt32() && method.Parameters[2].Type.IsInt32())
                    {
                        LastIndexOf_Char_Int32_Int32 = method;
                    }
                    else if (method.Parameters.Length == 2 && method.Parameters[0].Type.IsChar() && method.Parameters[1].Type.IsEqualTo(StringComparisonSymbol))
                    {
                        LastIndexOf_Char_StringComparison = method;
                    }
                }

                foreach (var method in stringSymbol.GetMembers(nameof(string.Join)).OfType<IMethodSymbol>())
                {
                    if (!method.IsStatic)
                        continue;

                    if (method.Parameters.Length == 2 && method.Parameters[0].Type.IsChar() && method.Parameters[1].Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Object })
                    {
                        Join_Char_ObjectArray = method;
                    }
                    else if (method.Parameters.Length == 2 && method.Parameters[0].Type.IsChar() && method.Parameters[1].Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_String })
                    {
                        Join_Char_StringArray = method;
                    }
                    else if (method.Parameters.Length == 2 && method.Parameters[0].Type.IsChar() && method.Parameters[1].Type is INamedTypeSymbol symbol && symbol.ConstructedFrom.IsEqualTo(EnumerableOfTSymbol))
                    {
                        Join_Char_IEnumerableT = method;
                    }
                    else if (method.Parameters.Length == 4 && method.Parameters[0].Type.IsChar() && method.Parameters[1].Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_String } && method.Parameters[2].Type.IsInt32() && method.Parameters[3].Type.IsInt32())
                    {
                        Join_Char_StringArray_Int32_Int32 = method;
                    }
                }
            }
        }

        public IMethodSymbol? StartsWith_Char { get; set; }
        public IMethodSymbol? EndsWith_Char { get; set; }

        public IMethodSymbol? Replace_Char_Char { get; set; }

        public IMethodSymbol? IndexOf_Char { get; set; }
        public IMethodSymbol? IndexOf_Char_Int32 { get; set; }
        public IMethodSymbol? IndexOf_Char_Int32_Int32 { get; set; }
        public IMethodSymbol? IndexOf_Char_StringComparison { get; set; }

        public IMethodSymbol? LastIndexOf_Char { get; set; }
        public IMethodSymbol? LastIndexOf_Char_Int32 { get; set; }
        public IMethodSymbol? LastIndexOf_Char_Int32_Int32 { get; set; }
        public IMethodSymbol? LastIndexOf_Char_StringComparison { get; set; }

        public IMethodSymbol? Join_Char_ObjectArray { get; set; }
        public IMethodSymbol? Join_Char_IEnumerableT { get; set; }
        public IMethodSymbol? Join_Char_StringArray { get; set; }
        public IMethodSymbol? Join_Char_StringArray_Int32_Int32 { get; set; }

        public INamedTypeSymbol? StringComparisonSymbol { get; set; }
        public ISymbol? StringComparison_Ordinal { get; set; }
        public ISymbol? StringComparison_CurrentCulture { get; set; }
        public ISymbol? StringComparison_InvariantCulture { get; set; }

        public INamedTypeSymbol? EnumerableOfTSymbol { get; set; }

        public void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var operation = (IInvocationOperation)context.Operation;
            if (operation.TargetMethod.ContainingType.IsString())
            {
                if (operation.TargetMethod.Name is "StartsWith")
                {
                    if (StartsWith_Char is null || operation.TargetMethod.IsEqualTo(StartsWith_Char))
                        return;

                    if (operation.Arguments.Length == 2)
                    {
                        if (operation.Arguments[0].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } } &&
                            operation.Arguments[1].Value is { ConstantValue: { HasValue: true, Value: (int)StringComparison.Ordinal } })
                        {
                            context.ReportDiagnostic(Rule, operation.Arguments[0].Value);
                        }
                    }
                }
                else if (operation.TargetMethod.Name is "EndsWith")
                {
                    if (EndsWith_Char is null || operation.TargetMethod.IsEqualTo(EndsWith_Char))
                        return;

                    if (operation.Arguments.Length == 2)
                    {
                        if (operation.Arguments[0].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } } &&
                            operation.Arguments[1].Value is { ConstantValue: { HasValue: true, Value: (int)StringComparison.Ordinal } })
                        {
                            context.ReportDiagnostic(Rule, operation.Arguments[0].Value);
                        }
                    }
                }
                else if (operation.TargetMethod.Name is "Replace")
                {
                    if (Replace_Char_Char is null || operation.TargetMethod.IsEqualTo(Replace_Char_Char))
                        return;

                    if (operation.Arguments.Length == 2)
                    {
                        if (operation.Arguments[0].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } } &&
                            operation.Arguments[1].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } })
                        {
                            // Improve the error message as the rule is reported on the method
                            context.ReportDiagnostic(Rule, ImmutableDictionary<string, string?>.Empty, operation, DiagnosticInvocationReportOptions.ReportOnMember);
                        }
                    }
                    else if (operation.Arguments.Length == 3)
                    {
                        if (operation.Arguments[0].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } } &&
                            operation.Arguments[1].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } } &&
                            operation.Arguments[2].Value is { ConstantValue: { HasValue: true, Value: (int)StringComparison.Ordinal } })
                        {
                            context.ReportDiagnostic(Rule, ImmutableDictionary<string, string?>.Empty, operation, DiagnosticInvocationReportOptions.ReportOnMember);
                        }
                    }
                }
                else if (operation.TargetMethod.Name is "IndexOf")
                {
                    if (operation.Arguments.Length == 2)
                    {
                        if (IndexOf_Char is not null)
                        {
                            if (operation.Arguments[0].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } } &&
                                operation.Arguments[1].Value is { ConstantValue: { HasValue: true, Value: (int)StringComparison.Ordinal } })
                            {
                                context.ReportDiagnostic(Rule, operation.Arguments[0].Value);
                                return;
                            }
                        }

                        if (IndexOf_Char_StringComparison is not null)
                        {
                            if (operation.Arguments[0].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } } &&
                                operation.Arguments[1].Value.Type.IsEqualTo(StringComparisonSymbol))
                            {
                                context.ReportDiagnostic(Rule, operation.Arguments[0].Value);
                                return;
                            }
                        }
                    }
                    else if (operation.Arguments.Length == 3)
                    {
                        if (IndexOf_Char_Int32 is null)
                            return;

                        if (operation.Arguments[0].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } } &&
                            operation.Arguments[1].Value.Type.IsInt32() &&
                            operation.Arguments[2].Value is { ConstantValue: { HasValue: true, Value: (int)StringComparison.Ordinal } })
                        {
                            context.ReportDiagnostic(Rule, operation.Arguments[0].Value);
                        }
                    }
                    else if (operation.Arguments.Length == 4)
                    {
                        if (IndexOf_Char_Int32_Int32 is null)
                            return;

                        if (operation.Arguments[0].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } } &&
                            operation.Arguments[1].Value.Type.IsInt32() &&
                            operation.Arguments[2].Value.Type.IsInt32() &&
                            operation.Arguments[3].Value is { ConstantValue: { HasValue: true, Value: (int)StringComparison.Ordinal } })
                        {
                            context.ReportDiagnostic(Rule, operation.Arguments[0].Value);
                        }
                    }
                }
                else if (operation.TargetMethod.Name is "LastIndexOf")
                {
                    if (operation.Arguments.Length == 2)
                    {
                        if (LastIndexOf_Char is not null)
                        {
                            if (operation.Arguments[0].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } } &&
                                operation.Arguments[1].Value is { ConstantValue: { HasValue: true, Value: (int)StringComparison.Ordinal } })
                            {
                                context.ReportDiagnostic(Rule, operation.Arguments[0].Value);
                                return;
                            }
                        }

                        if (LastIndexOf_Char_StringComparison is not null)
                        {
                            if (operation.Arguments[0].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } } &&
                                operation.Arguments[1].Value.Type.IsEqualTo(StringComparisonSymbol))
                            {
                                context.ReportDiagnostic(Rule, operation.Arguments[0].Value);
                                return;
                            }
                        }
                    }
                    else if (operation.Arguments.Length == 3)
                    {
                        if (LastIndexOf_Char_Int32 is null)
                            return;

                        if (operation.Arguments[0].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } } &&
                            operation.Arguments[1].Value.Type.IsInt32() &&
                            operation.Arguments[2].Value is { ConstantValue: { HasValue: true, Value: (int)StringComparison.Ordinal } })
                        {
                            context.ReportDiagnostic(Rule, operation.Arguments[0].Value);
                        }
                    }
                    else if (operation.Arguments.Length == 4)
                    {
                        if (LastIndexOf_Char_Int32_Int32 is null)
                            return;

                        if (operation.Arguments[0].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } } &&
                            operation.Arguments[1].Value.Type.IsInt32() &&
                            operation.Arguments[2].Value.Type.IsInt32() &&
                            operation.Arguments[3].Value is { ConstantValue: { HasValue: true, Value: (int)StringComparison.Ordinal } })
                        {
                            context.ReportDiagnostic(Rule, operation.Arguments[0].Value);
                        }
                    }
                }
                else if (operation.TargetMethod.Name is "Join" && operation.TargetMethod.IsStatic)
                {
                    if (operation.Arguments.Length > 1)
                    {
                        if (operation.Arguments[0].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } })
                        {
                            var secondParameterType = operation.TargetMethod.Parameters[1].Type;
                            switch (operation.Arguments.Length)
                            {
                                case 2:
                                    if (Join_Char_ObjectArray is not null && secondParameterType is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Object })
                                    {
                                        context.ReportDiagnostic(Rule, operation.Arguments[0].Value);
                                        return;
                                    }

                                    if (Join_Char_StringArray is not null && secondParameterType is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_String })
                                    {
                                        context.ReportDiagnostic(Rule, operation.Arguments[0].Value);
                                        return;
                                    }

                                    if (Join_Char_IEnumerableT is not null && secondParameterType is INamedTypeSymbol symbol && symbol.ConstructedFrom.IsEqualTo(EnumerableOfTSymbol))
                                    {
                                        context.ReportDiagnostic(Rule, operation.Arguments[0].Value);
                                        return;
                                    }

                                    break;

                                case 4:
                                    if (Join_Char_StringArray_Int32_Int32 is not null)
                                    {
                                        context.ReportDiagnostic(Rule, operation.Arguments[0].Value);
                                        return;
                                    }

                                    break;
                            }
                        }
                    }
                    else if (operation.Arguments.Length == 4)
                    {
                        if (Join_Char_StringArray_Int32_Int32 is not null)
                        {
                            if (operation.Arguments[0].Value is { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string { Length: 1 } } })
                            {
                                context.ReportDiagnostic(Rule, operation.Arguments[0].Value);
                                return;
                            }
                        }
                    }
                }
            }
        }
    }
}
