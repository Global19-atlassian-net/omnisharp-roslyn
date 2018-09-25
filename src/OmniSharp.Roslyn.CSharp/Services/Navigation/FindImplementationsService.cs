using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using OmniSharp.Extensions;
using OmniSharp.Helpers;
using OmniSharp.Mef;
using OmniSharp.Models;
using OmniSharp.Models.FindImplementations;

namespace OmniSharp.Roslyn.CSharp.Services.Navigation
{
    [OmniSharpHandler(OmniSharpEndpoints.FindImplementations, LanguageNames.CSharp)]
    public class FindImplementationsService : IRequestHandler<FindImplementationsRequest, QuickFixResponse>
    {
        private readonly OmniSharpWorkspace _workspace;

        [ImportingConstructor]
        public FindImplementationsService(OmniSharpWorkspace workspace)
        {
            _workspace = workspace;
        }

        public async Task<QuickFixResponse> Handle(FindImplementationsRequest request)
        {
            var document = _workspace.GetDocument(request.FileName);
            var response = new QuickFixResponse();

            if (document != null)
            {
                var semanticModel = await document.GetSemanticModelAsync();
                var sourceText = await document.GetTextAsync();
                var position = sourceText.Lines.GetPosition(new LinePosition(request.Line, request.Column));

                var quickFixes = new List<QuickFix>();
                var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, _workspace);

                // SymbolFinder.FindImplementationsAsync will not include the method overrides
                var implementations = await SymbolFinder.FindImplementationsAsync(symbol, _workspace.CurrentSolution);
                foreach (var implementation in implementations)
                {
                    quickFixes.Add(implementation, _workspace);

                    if (implementation.IsOverridable())
                    {
                        var overrides = await SymbolFinder.FindOverridesAsync(implementation, _workspace.CurrentSolution);
                        quickFixes.AddRange(overrides, _workspace);
                    }
                }

                // for types also include derived classes
                // for other symbols, find overrides and include those
                if (symbol is INamedTypeSymbol namedTypeSymbol)
                {
                    var derivedTypes = await SymbolFinder.FindDerivedClassesAsync(namedTypeSymbol, _workspace.CurrentSolution);
                    quickFixes.AddRange(derivedTypes, _workspace);
                }
                else if (symbol.IsOverridable())
                {
                    var overrides = await SymbolFinder.FindOverridesAsync(symbol, _workspace.CurrentSolution);
                    quickFixes.AddRange(overrides, _workspace);
                }

                // also include the original declaration of the symbol
                if (!symbol.IsAbstract)
                {
                    // for partial methods, pick the one with body
                    if (symbol is IMethodSymbol method)
                    {
                        symbol = method.PartialImplementationPart ?? symbol;
                    }

                    var location = symbol.Locations.First();
                    quickFixes.Add(location, _workspace);
                }

                response = new QuickFixResponse(quickFixes.OrderBy(q => q.FileName)
                                                            .ThenBy(q => q.Line)
                                                            .ThenBy(q => q.Column));
            }

            return response;
        }
    }
}
