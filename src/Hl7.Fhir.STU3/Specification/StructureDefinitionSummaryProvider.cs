/* 
 * Copyright (c) 2018, Firely (info@fire.ly) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://github.com/FirelyTeam/firely-net-sdk/blob/master/LICENSE
 */

using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Navigation;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using Tasks = System.Threading.Tasks;

namespace Hl7.Fhir.Specification
{
    public class StructureDefinitionSummaryProvider : IStructureDefinitionSummaryProvider
    {
        public delegate bool TypeNameMapper(string typeName, out string canonical);

        private readonly IAsyncResourceResolver _resolver;
        private readonly TypeNameMapper _typeNameMapper;

        public static bool DefaultTypeNameMapper(string name, out string canonical)
        {
            canonical = ResourceIdentity.CORE_BASE_URL + name;
            return true;
        }

#pragma warning disable CS0618 // Type or member is obsolete
        public StructureDefinitionSummaryProvider(ISyncOrAsyncResourceResolver resolver, TypeNameMapper mapper = null)
#pragma warning restore CS0618 // Type or member is obsolete
        {
            _resolver = resolver.AsAsync();
            _typeNameMapper = mapper ?? DefaultTypeNameMapper;
        }

        public async Tasks.Task<IStructureDefinitionSummary> ProvideAsync(string canonical)
        {
            var isLocalType = !canonical.Contains("/");
            string mappedCanonical = canonical;

            if (isLocalType)
            {
                var mapSuccess = _typeNameMapper(canonical, out mappedCanonical);
                if (!mapSuccess) return null;
            }

            var sd = await _resolver.FindStructureDefinitionAsync(mappedCanonical).ConfigureAwait(false);

            return sd is null ?
                null
                : (IStructureDefinitionSummary)new StructureDefinitionComplexTypeSerializationInfo(ElementDefinitionNavigator.ForSnapshot(sd));
        }

        [Obsolete("StructureDefinitionSummaryProvider now works best with asynchronous resolvers. Use ProvideAsync() instead.")]
        public IStructureDefinitionSummary Provide(string canonical) =>
            //TaskHelper.Await(() => ProvideAsync(canonical));
            ProvideAsync(canonical).GetAwaiter().GetResult();
    }

    internal struct BackboneElementComplexTypeSerializationInfo : IStructureDefinitionSummary
    {
        private readonly ElementDefinitionNavigator _nav;

        public BackboneElementComplexTypeSerializationInfo(ElementDefinitionNavigator nav)
        {
            this._nav = nav;
        }

        public string TypeName => _nav.Current.Type[0].Code;

        public bool IsAbstract => true;

        public bool IsResource => false;

        public IReadOnlyCollection<IElementDefinitionSummary> GetElements() =>
            StructureDefinitionComplexTypeSerializationInfo.getElements(_nav).ToList();
    }

    internal struct StructureDefinitionComplexTypeSerializationInfo : IStructureDefinitionSummary
    {
        private readonly ElementDefinitionNavigator _nav;

        public StructureDefinitionComplexTypeSerializationInfo(ElementDefinitionNavigator nav)
        {
            this._nav = nav;
        }

        public string TypeName => _nav.StructureDefinition.Name;

        public bool IsAbstract => _nav.StructureDefinition.Abstract ?? false;

        public bool IsResource => _nav.StructureDefinition.Kind == StructureDefinition.StructureDefinitionKind.Resource;

        public IReadOnlyCollection<IElementDefinitionSummary> GetElements()
        {
            if (_nav.Current == null && !_nav.MoveToFirstChild())
                return new IElementDefinitionSummary[0];

            return getElements(_nav).ToList();
        }

        private static bool isPrimitiveValueConstraint(ElementDefinition ed) => ed.Path.EndsWith(".value") && ed.Type.All(t => t.Code == null);

        internal static IEnumerable<IElementDefinitionSummary> getElements(ElementDefinitionNavigator nav)
        {
            string lastName = "";

            var bookmark = nav.Bookmark();

            try
            {
                if (!nav.MoveToFirstChild()) yield break;

                do
                {
                    if (nav.PathName == lastName) continue;    // ignore slices
                    if (isPrimitiveValueConstraint(nav.Current)) continue;      // ignore value attribute

                    lastName = nav.PathName;
                    yield return new ElementDefinitionSerializationInfo(nav.ShallowCopy());
                }
                while (nav.MoveToNext());
            }
            finally
            {
                nav.ReturnToBookmark(bookmark);
            }
        }
    }

    internal struct TypeReferenceInfo : IStructureDefinitionReference
    {
        private readonly string _referencedType;

        public TypeReferenceInfo(string referencedType)
        {
            _referencedType = referencedType;
        }

        public string ReferredType => _referencedType;
    }


    internal struct ElementDefinitionSerializationInfo : IElementDefinitionSummary
    {
        private readonly Lazy<ITypeSerializationInfo[]> _types;
        private readonly ElementDefinition _definition;

        internal ElementDefinitionSerializationInfo(ElementDefinitionNavigator nav)
        {
            if (nav == null || nav.Current == null) throw Error.ArgumentNull(nameof(nav));

            _types = new Lazy<ITypeSerializationInfo[]>(() => buildTypes(nav));
            ElementName = noChoiceSuffix(nav.PathName);
            _definition = nav.Current;
            Order = nav.OrdinalPosition.Value;     // cannot be null, since nav.Current != null

            static string noChoiceSuffix(string n) => n.EndsWith("[x]") ? n.Substring(0, n.Length - 3) : n;
        }

        private static ITypeSerializationInfo[] buildTypes(ElementDefinitionNavigator nav)
        {
            if (nav.Current.IsBackboneElement())
                return new[] { (ITypeSerializationInfo)new BackboneElementComplexTypeSerializationInfo(nav) };
            else if (nav.Current.ContentReference != null)
            {
                var reference = nav.ShallowCopy();
                var name = nav.Current.ContentReference;

                // Note that these contentReferences MAY be absolute urls in profiles, but since this provider works on the
                // core SDs only, we do not have to take this into account.
                if (!reference.JumpToNameReference(name))
                    throw Error.InvalidOperation($"StructureDefinition '{nav?.StructureDefinition?.Url}' " +
                        $"has a namereference '{name}' on element '{nav.Current.Path}' that cannot be resolved.");

                return new[] { (ITypeSerializationInfo)new BackboneElementComplexTypeSerializationInfo(reference) };
            }
            else
                return nav.Current.Type.Select(t => (ITypeSerializationInfo)new TypeReferenceInfo(t.Code)).Distinct().ToArray();
        }

        public string ElementName { get; private set; }

        public bool IsCollection => _definition.IsRepeating();

        public bool InSummary => _definition.IsSummary ?? false;

        /// <inheritdoc/>
        public bool IsModifier => _definition.IsModifier ?? false;

        public bool IsRequired => (_definition.Min ?? 0) >= 1;

        public XmlRepresentation Representation
        {
            get
            {
                if (!_definition.Representation.Any()) return XmlRepresentation.XmlElement;

                return _definition.Representation.First() switch
                {
                    ElementDefinition.PropertyRepresentation.XmlAttr => XmlRepresentation.XmlAttr,
                    ElementDefinition.PropertyRepresentation.XmlText => XmlRepresentation.XmlText,
                    ElementDefinition.PropertyRepresentation.TypeAttr => XmlRepresentation.TypeAttr,
                    ElementDefinition.PropertyRepresentation.CdaText => XmlRepresentation.CdaText,
                    ElementDefinition.PropertyRepresentation.Xhtml => XmlRepresentation.XHtml,
                    _ => XmlRepresentation.XmlElement,
                };
            }
        }

        public bool IsChoiceElement => _definition.IsChoice();

        public int Order { get; private set; }

        public bool IsResource => isResource(_definition);

        // TODO: This is actually not complete: the Type might be any subclass of Resource (including DomainResource), but this will
        // do for all current situations. I will regret doing this at some point in the future.
        private static bool isResource(ElementDefinition defn) => defn.Type.Count == 1 &&
            (defn.Type[0].Code == "Resource" || defn.Type[0].Code == "DomainResource");

        public string DefaultTypeName =>
            _definition.GetStringExtension("http://hl7.org/fhir/StructureDefinition/elementdefinition-defaulttype");

        public ITypeSerializationInfo[] Type => _types.Value;

        public string NonDefaultNamespace =>
            _definition.GetStringExtension("http://hl7.org/fhir/StructureDefinition/elementdefinition-namespace");
    }
}