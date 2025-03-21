/* 
 * Copyright (c) 2018, Firely (info@fire.ly) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://raw.githubusercontent.com/FirelyTeam/firely-net-sdk/master/LICENSE
 */

// [WMR 20170411] HACK - suppress infinite recursion
// TODO: Properly handle recursive type declarations
// Don't throw exception but emit OperationOutcome issue(s) and continue
#define HACK_STU3_RECURSION

// [WMR 20190822] R4: Custom element Ids are no longer allowed/supported
// http://hl7.org/fhir/elementdefinition.html#id
// #define CUSTOM_ELEMENT_IDS

// [WMR 20190828] Auto-generate slice names for type slices if missing from the diff
// Note: Also defined by ElementMatcher class; must define/undefine both
#define GENERATE_MISSING_TYPE_SLICE_NAMES

// [WMR 20190828] R4: Normalize renamed type slices in snapshot
// e.g. diff: "valueString" => snap: "value[x]:valueString"
#define NORMALIZE_RENAMED_TYPESLICE

using FluentAssertions;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification.Navigation;
using Hl7.Fhir.Specification.Snapshot;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Tests.Snapshot;
using Hl7.Fhir.Support;
using Hl7.Fhir.Utility;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using NSubstitute.Exceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Tasks = System.Threading.Tasks;

namespace Hl7.Fhir.Specification.Tests
{
    [TestClass, TestCategory("Snapshot")]
    public partial class SnapshotGeneratorTest2
    {
        private SnapshotGenerator _generator;
        private IResourceResolver _standardFhirSource;
        private CachedResolver _testResolver;
        private TimingSource _source;
        private readonly SnapshotGeneratorSettings _settings = new()
        {
            // Throw on unresolved profile references; must include in TestData folder
            GenerateSnapshotForExternalProfiles = true,
            ForceRegenerateSnapshots = true,
            GenerateExtensionsOnConstraints = false,
            GenerateAnnotationsOnConstraints = false,
            GenerateElementIds = true // STU3
        };

        [TestInitialize]
        public void Setup()
        {
            var dirSource = new DirectorySource("TestData/snapshot-test", new DirectorySourceSettings { IncludeSubDirectories = true });
            _source = new TimingSource(dirSource);
            // [WMR 20170810] Order is important!
            // Specify source first to override core defs from
            // TestData\snapshot-test\profiles-resources.xml and profiles-types.xml
            _standardFhirSource = ZipSource.CreateValidationSource();
            _testResolver = new CachedResolver(new MultiResolver(_standardFhirSource, _source));
        }

        private StructureDefinition createStructureDefinition(string url, params ElementDefinition[] elements)
        {
            return new StructureDefinition
            {
                Url = url,
                Name = "name",
                Status = PublicationStatus.Draft,
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Abstract = false,
                Type = "Practitioner",
                Differential = new StructureDefinition.DifferentialComponent
                {
                    Element = elements.ToList()
                }
            };
        }

        [DataTestMethod]
        [DataRow(null, null, null)]
        [DataRow(null, "1", "1")]
        [DataRow("1", null, "1")]
        [DataRow("1", "1", "1")]
        [DataRow("1", "*", "1")]
        [DataRow("2", "*", "2")]
        [DataRow("*", "*", "*")]
        [DataRow("*", "2", "2")]
        [DataRow("*", null, "*")]
        [DataRow(null, "*", "*")]
        [DataRow("3", "2", "2")]
        [DataRow("2", "3", "2")]
        public void TestMergeMax(string snap, string diff, string expected)
        {
            var sg = new SnapshotGenerator.ElementDefnMerger();

            var actual = sg.mergeMax(new FhirString(snap), new FhirString(diff));
            Assert.AreEqual(expected, actual.Value);
        }

        [DataTestMethod]
        [DataRow(null, null, null)]
        [DataRow(null, "1", "1")]
        [DataRow(null, "2", "2")]
        [DataRow(null, "3", "3")]
        [DataRow(null, "*", "*")]
        [DataRow(0, null, "0")]
        [DataRow(0, "1", "1")]
        [DataRow(0, "2", "2")]
        [DataRow(0, "3", "3")]
        [DataRow(0, "*", "*")]
        [DataRow(2, null, "2")]
        [DataRow(2, "1", "2")]
        [DataRow(2, "2", "2")]
        [DataRow(2, "3", "3")]
        [DataRow(2, "*", "*")]
        [DataRow(4, null, "4")]
        [DataRow(4, "1", "4")]
        [DataRow(4, "2", "4")]
        [DataRow(4, "3", "4")]
        [DataRow(4, "*", "*")]
        public void TestConstrainMax(int? snapMin, string snapMax, string expected)
        {
            var actual = SnapshotGenerator.ElementDefnMerger.constrainMax(new FhirString(snapMax), new UnsignedInt(snapMin));
            Assert.AreEqual(expected, actual.Value);
        }

        [DataTestMethod]
        [DataRow(null, null, null)]
        [DataRow(null, 1, 1)]
        [DataRow(1, null, 1)]
        [DataRow(1, 2, 2)]
        [DataRow(2, 1, 2)]
        [DataRow(1, 1, 1)]
        public void TestMergeMin(int? snap, int? diff, int? expected)
        {
            var sg = new SnapshotGenerator.ElementDefnMerger();

            var actual = sg.mergeMin(new UnsignedInt(snap), new UnsignedInt(diff));
            Assert.AreEqual(expected, actual.Value);
        }

        [TestMethod]
        public async Tasks.Task OverriddenNestedStructureDefinitionLists()
        {
            var baseCanonical = "http://yourdomain.org/fhir/StructureDefinition/Base";
            var code = "someCode";
            var discriminatorPath = "system";

            var baseSD = createStructureDefinition(baseCanonical,
                new ElementDefinition
                {
                    Path = "Practitioner.identifier",
                    Slicing = new ElementDefinition.SlicingComponent
                    {
                        Rules = ElementDefinition.SlicingRules.Open,
                        Discriminator = new List<ElementDefinition.DiscriminatorComponent>
                        {
                            new ElementDefinition.DiscriminatorComponent
                            {
                                Type = ElementDefinition.DiscriminatorType.Value,
                                Path = discriminatorPath
                            }
                        }
                    }
                },
                new ElementDefinition
                {
                    Path = "Practitioner.identifier:test",
                    SliceName = "test",
                    Condition = new[] { "http://system.org" },
                    Code = new List<Coding>
                    {
                        new Coding{Code = code}
                    }
                });

            var derivedSD = createStructureDefinition("http://yourdomain.org/fhir/StructureDefinition/Derived",
                new ElementDefinition
                {
                    Path = "Practitioner.identifier",
                    Slicing = new ElementDefinition.SlicingComponent
                    {
                        Rules = ElementDefinition.SlicingRules.Closed
                    }
                },
                new ElementDefinition
                {
                    Path = "Practitioner.identifier:test"
                });
            derivedSD.BaseDefinition = baseSD.Url;

            var resourceResolver = Substitute.For<IResourceResolver>();
            resourceResolver.ResolveByCanonicalUri(Arg.Any<string>()).Returns(baseSD);
            var snapshotGenerator = new SnapshotGenerator(resourceResolver, new SnapshotGeneratorSettings());
            await snapshotGenerator.UpdateAsync(derivedSD);

            derivedSD.Snapshot.Element.Single(element => element.Path == "Practitioner.identifier").Slicing.Discriminator.First().Path.Should().Be(discriminatorPath, "The discriminator should be copied from base");
            derivedSD.Snapshot.Element.Single(element => element.Path == "Practitioner.identifier:test").Code.First().Code.Should().Be(code, "The code should be copied from base");
        }

        [TestMethod]
        public async Tasks.Task GenerateExtensionSnapshot()
        {
            var sd = await _testResolver.FindStructureDefinitionAsync(@"http://fhir.nl/fhir/StructureDefinition/nl-core-address-official");

            Assert.IsNotNull(sd);
            // dumpReferences(sd);

            var (_, expanded) = await generateSnapshotAndCompare(sd);
            dumpOutcome(_generator.Outcome);
            dumpBasePaths(expanded);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);
            Assert.IsNull(_generator.Outcome);

            var elems = expanded.Snapshot.Element;

            // [WMR 20190211] FIXED
            // STU3: "valueBoolean" replaces "value[x]" in snapshot
            //Assert.AreEqual(5, elems.Count);
            // R4: snapshot contains both "value[x]" and "valueBoolean" 
            Assert.AreEqual(6, elems.Count);

            Assert.AreEqual("Extension", elems[0].Path);
            Assert.AreEqual("Extension.id", elems[1].Path);
            Assert.AreEqual("Extension.extension", elems[2].Path);
            Assert.AreEqual("Extension.url", elems[3].Path);
            Assert.AreEqual(expanded.Url, (elems[3].Fixed as FhirUri)?.Value);

            // STU3
            //Assert.AreEqual("Extension.valueBoolean", elems[4].Path);
            // R4
            Assert.AreEqual("Extension.value[x]", elems[4].Path);
#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.AreEqual("Extension.value[x]", elems[5].Path);
            Assert.AreEqual("valueBoolean", elems[5].SliceName);
#else
            //Assert.AreEqual("Extension.valueBoolean", elems[5].Path);
#endif
        }

        [TestMethod]
        public async Tasks.Task TestConstraintSources()
        {
            var dom = await _testResolver.FindStructureDefinitionAsync("http://hl7.org/fhir/StructureDefinition/DomainResource");
            Assert.IsNotNull(dom);
            await generateSnapshotAndCompare(dom);
            Assert.IsTrue(dom.Snapshot?.Element
                          .Where(e => e.Path == "DomainResource.extension").FirstOrDefault()
                          .Constraint.Any(c => c.Key == "ext-1" && c.Source == "http://hl7.org/fhir/StructureDefinition/Extension") == true);

            Assert.IsTrue(dom.Snapshot?.Element
                          .Where(e => e.Path == "DomainResource.extension").FirstOrDefault()
                          .Constraint.Any(c => c.Key == "ele-1" && c.Source == "http://hl7.org/fhir/StructureDefinition/Element") == true);


            var pat = await _testResolver.FindStructureDefinitionAsync("http://hl7.org/fhir/StructureDefinition/Patient");
            Assert.IsNotNull(pat);
            await generateSnapshotAndCompare(pat);
            Assert.IsTrue(pat.Snapshot?.Element
                          .Where(e => e.Path == "Patient").FirstOrDefault()
                          .Constraint.Any(c => c.Key == "dom-2" && c.Source == "http://hl7.org/fhir/StructureDefinition/DomainResource") == true);

        }

        [TestMethod]
        public async Tasks.Task GenerateSnapshotForExternalProfiles()
        {
            //Test external type profile
            var sd = await _testResolver.FindStructureDefinitionAsync(@"http://issue.com/fhir/StructureDefinition/MyPatient");
            Assert.IsNotNull(sd);
            _settings.GenerateSnapshotForExternalProfiles = false;
            _generator = new SnapshotGenerator(_testResolver, _settings);
            await _generator.UpdateAsync(sd);
            Assert.IsNotNull(sd.Snapshot);

            var sdRef = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyHumanName");
            Assert.IsNull(sdRef.Snapshot);
            dumpOutcome(_generator.Outcome);

            _settings.GenerateSnapshotForExternalProfiles = true;
            _generator = new SnapshotGenerator(_testResolver, _settings);
            await _generator.UpdateAsync(sd);

            sdRef = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyHumanName");
            Assert.IsNotNull(sdRef.Snapshot);
            dumpOutcome(_generator.Outcome);


            //Test external base profile
            var sdDerived = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyDerivedPatient");
            Assert.IsNotNull(sdDerived);

            _settings.GenerateSnapshotForExternalProfiles = false;
            _generator = new SnapshotGenerator(_testResolver, _settings);
            await _generator.UpdateAsync(sdDerived);
            Assert.IsNotNull(sdDerived.Snapshot);

            var sdBase = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyBase");
            Assert.IsNull(sdBase.Snapshot);
            dumpOutcome(_generator.Outcome);

            _settings.GenerateSnapshotForExternalProfiles = true;
            _generator = new SnapshotGenerator(_testResolver, _settings);
            await _generator.UpdateAsync(sdDerived);

            sdBase = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyBase");
            Assert.IsNotNull(sdBase.Snapshot);
            dumpOutcome(_generator.Outcome);
        }

        [TestMethod]
        public async Tasks.Task GenerateSingleSnapshot()
        {
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/daf-condition");
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/xdsdocumentreference");
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/gao-medicationorder");
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/shareablevalueset");
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/gao-alternate");
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/gao-result");
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/gao-procedurerequest");
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/cqif-guidanceartifact");

            // [WMR 20160825] Examples by Simone Heckman - custom, free-form canonical url
            // => ResourceIdentity is obsolete!
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://fhir.de/StructureDefinition/kbv/betriebsstaette");
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://fhir.de/StructureDefinition/kbv/istNebenbetriebsstaette");

            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyBasic");

            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyObservation2");

            // [WMR 20161219] Problem: Composition.section element in core resource has name 'section' (b/o name reference)
            // Ambiguous... snapshot generator slicing logic cannot handle this...

            // [WMR 20161222] Example by EK from validator
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/StructureDefinition/DocumentComposition");
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/Composition");

            // [WMR 20170110] Test problematic extension
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/us-core-direct");

            var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/Account");

            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/PatientWithExtension");

            Assert.IsNotNull(sd);

            // dumpReferences(sd);

            var (_, expanded) = await generateSnapshotAndCompare(sd);

            dumpOutcome(_generator.Outcome);
            // dumpBasePaths(expanded);
            expanded.Snapshot.Element.Dump();
        }

        [TestMethod]
        public async Tasks.Task TestChoiceTypeWithMultipleProfileConstraints()
        {
            // [WMR 20161005] The following profile defines several type constraints on Observation.value[x]
            // - Type = Quantity, Profile = WeightQuantity
            // - Type = Quantity, Profile = HeightQuantity
            // - Type = string
            // The snapshot generator should support this without any issues.

            // var tempPath = Path.GetTempPath();
            // var validationTestProfiles = (new Validation.TestProfileArtifactSource()).TestProfiles;
            // var sdHeightQty = validationTestProfiles.FirstOrDefault(s => s.Url == "http://validationtest.org/fhir/StructureDefinition/HeightQuantity");
            // File.WriteAllText(Path.Combine(tempPath, "HeightQuantity.StructureDefinition.xml"), FhirSerializer.SerializeResourceToXml(sdHeightQty));
            // var sdWeightQty = validationTestProfiles.FirstOrDefault(s => s.Url == "http://validationtest.org/fhir/StructureDefinition/WeightQuantity");
            // File.WriteAllText(Path.Combine(tempPath, "WeightQuantity.StructureDefinition.xml"), FhirSerializer.SerializeResourceToXml(sdWeightQty));

            var sd = await _testResolver.FindStructureDefinitionAsync(@"http://validationtest.org/fhir/StructureDefinition/WeightHeightObservation");

            Assert.IsNotNull(sd);

            // dumpReferences(sd);

            var (_, expanded) = await generateSnapshotAndCompare(sd);

            dumpOutcome(_generator.Outcome);
            dumpBasePaths(expanded);
        }

        [TestMethod]
        public async Tasks.Task GenerateRepeatedSnapshot()
        {
            // [WMR 20161005] This generated exceptions in an early version of the snapshot generator (fixed)

            var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/MeasureReport");
            (_, var expanded) = await generateSnapshotAndCompare(sd);
            dumpOutcome(_generator.Outcome);
            dumpBasePaths(expanded);

            sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/clinicaldocument");
            (_, expanded) = await generateSnapshotAndCompare(sd);
            dumpOutcome(_generator.Outcome);
            dumpBasePaths(expanded);
        }

        // [WMR 20170424] For debugging SnapshotBaseComponentGenerator
        [TestMethod]
        public async Tasks.Task TestFullyExpandCoreOrganization()
        {
            // [WMR 20161005] This simulates custom Forge post-processing logic
            // i.e. perform a regular snapshot expansion, then explicitly expand all complex elements (esp. those without any differential constraints)

            var sd = await _testResolver.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.Organization);
            Assert.IsNotNull(sd);
            await generateSnapshot(sd);
            Assert.IsTrue(sd.HasSnapshot);
            _ = sd.Snapshot.Element;

            var issues = _generator.Outcome?.Issue ?? new List<OperationOutcome.IssueComponent>();
            var expanded = await fullyExpand(sd.Snapshot.Element, issues);

            Assert.IsNotNull(expanded);
            dumpBaseElems(expanded);

            var identifierValueElement = expanded.Single(element => element.Path == "Organization.identifier.value");
            identifierValueElement.Extension.Should().BeEmpty("Extensions on the value type should not be inherited");

            Assert.IsNull(_generator.Outcome);
        }

        // [WMR 20180115] NEW - Replacement for expandAllComplexElements (OBSOLETE)
        // Expand all elements with complex type and no children
        private async Tasks.Task<List<ElementDefinition>> fullyExpand(IList<ElementDefinition> elements, List<OperationOutcome.IssueComponent> issues = null)
        {
            var nav = new ElementDefinitionNavigator(elements);
            // Skip root element
            if (nav.MoveToFirstChild())
            {
                if (_generator == null)
                {
                    _generator = new SnapshotGenerator(_testResolver, _settings);
                }
                await fullyExpandElement(nav, issues);
                return nav.Elements.ToList();
            }
            return elements.ToList();
        }

        // Expand current element if it has a complex type and no children (recursively)
        private async Tasks.Task fullyExpandElement(ElementDefinitionNavigator nav, List<OperationOutcome.IssueComponent> issues)
        {
            if (nav.HasChildren || (isExpandableElement(nav.Current) && await _generator.ExpandElementAsync(nav)))
            {
                if (issues != null && _generator.Outcome != null)
                {
                    issues.AddRange(_generator.Outcome.Issue);
                }

                Debug.WriteLine($"[{nameof(fullyExpandElement)}] " + nav.Path);
                var bm = nav.Bookmark();
                if (nav.MoveToFirstChild())
                {
                    do
                    {
                        await fullyExpandElement(nav, issues);
                    } while (nav.MoveToNext());
                    Assert.IsTrue(nav.ReturnToBookmark(bm));
                }
            }
        }

        private static bool isExpandableElement(ElementDefinition element)
        {
#if HACK_STU3_RECURSION
            // [WMR 20170328] DEBUG HACK
            // Prevent recursion:
            // - Identifier.assigner : Reference
            // - Reference.identifier : Identifier
            if (element.Path == "Reference.identifier"
                || element.Base?.Path == "Reference.identifier"
                // [WMR 20170424] Added
                || (element.Base?.Path.EndsWith(".reference.identifier") ?? false)
                || (element.Base?.Path == "Identifier.assigner.identifier")
                || (element.Base?.Path.EndsWith(".identifier.assigner.identifier") ?? false)
            )
            {
                Debug.Print($"[{nameof(isExpandableElement)}] RECURSION HACK: skip expansion for element: '{element.Path}'");
                return false;
            }
#endif

            var type = element.PrimaryType();

            if (type == null || element.Type.Select(t => t.Code).Distinct().Count() != 1) { return false; }

            var typeName = type?.Code;
            return !String.IsNullOrEmpty(typeName)
                   // Expand complex datatypes and resources
                   && isComplexDataTypeOrResource(typeName)
                   && (
                        // Only expand extension elements with a custom name or profile
                        // Do NOT expand the core Extension.extension element, as this will trigger infinite recursion
                        typeName != FHIRAllTypes.Extension.GetLiteral()
                        || type.Profile.Any()
                        || element.SliceName != null
                   );
        }

        // [WMR 20180116] Returns true for complex datatypes and resources, or false otherwise
        private static bool isComplexDataTypeOrResource(string typeName) => !ModelInfo.IsPrimitive(typeName);

        private static bool isComplexDataTypeOrResource(FHIRAllTypes type) => !ModelInfo.IsPrimitive(type);


        // [WMR 20180115] NEW - Use alternative (iterative) approach for full expansion
        [TestMethod]
        public async Tasks.Task TestFullyExpandCorePatient()
        {
            // [WMR 20180115] Iteratively expand all complex elements
            // 1. First generate regular snapshot
            // 2. Re-iterate elements, expand complex elements w/o children (recursively)

            var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/Patient");
            Assert.IsNotNull(sd);

            (_, var snapshot) = await generateSnapshotAndCompare(sd);
            Assert.IsNotNull(snapshot);
            Assert.IsTrue(snapshot.HasSnapshot);

            var snapElems = snapshot.Snapshot.Element;
            Debug.WriteLine($"Default snapshot: {snapElems.Count} elements");
            dumpBaseElems(snapElems);
            // [WMR 20181212] R4 FIXED - Patient.animal has been removed, including children
            // Total of 7 elements removed:
            // -1 (animal)
            // -3 inline children
            // -3 inherited children (id, extension, modifierExtension)
            //Assert.AreEqual(52, snapElems.Count);
            Assert.AreEqual(45, snapElems.Count);

            var issues = _generator.Outcome?.Issue ?? new List<OperationOutcome.IssueComponent>();
            var fullElems = await fullyExpand(snapElems, issues);
            Debug.WriteLine($"Full expansion: {fullElems.Count} elements");
            dumpBaseElems(fullElems);
            //Assert.AreEqual(310, fullElems.Count);
            // [WMR 20181212] R4 FIXED
            // Total of 7 + 3 * 12 = 43 elements removed:
            // -1 (animal)
            // -3 inline children, of type CodeableConcept
            // -3*12 full expansion of CodeableConcept
            // -3 inherited children (id, extension, modifierExtension)
            // MV 20200908 R5 fixed
            // +5 photo (Attachment): extra attributes (TU): heigth, width, frames, duration, pages 
            fullElems.Count.Should().BeGreaterThan(snapElems.Count);
            //Assert.AreEqual(282, fullElems.Count);
            Assert.AreEqual(issues.Count, 0);

            // Verify
            for (int j = 1; j < fullElems.Count; j++)
            {
                if (isExpandableElement(fullElems[j]))
                {
                    await verifyExpandElement(fullElems[j], fullElems, fullElems);
                }
            }
        }

        // [WMR 20180115] NEW - Use alternative (iterative) approach for full expansion
        // Note: result is different from TestCoreOrganizationNL, contains more elements - correct!
        // Older approach was flawed, e.g. see exclusion for Organization.type
        [TestMethod]
#if R5
        [Ignore("Resource Organization has changed in number of elements")]
#endif
        public async Tasks.Task TestFullyExpandNLCoreOrganization()
        {
            // core-organization-nl references extension core-address-nl
            // BUG: expanded extension child elements have incorrect .Base.Path ...?!
            // e.g. Organization.address.type - Base = Organization.address.use
            // Fixed by adding conditional to copyChildren

            var sd = await _testResolver.FindStructureDefinitionAsync(@"http://fhir.nl/fhir/StructureDefinition/nl-core-organization");
            Assert.IsNotNull(sd);

            StructureDefinition snapshot = null;
            // generateSnapshotAndCompare(sd, out snapshot);

            _generator = new SnapshotGenerator(_testResolver, _settings);
            _generator.PrepareElement += elementHandler;
            try
            {
                (_, snapshot) = await generateSnapshotAndCompare(sd);

                Assert.IsNotNull(snapshot);
                Assert.IsTrue(snapshot.HasSnapshot);

                var snapElems = snapshot.Snapshot.Element;
                Debug.WriteLine($"Default snapshot: {snapElems.Count} elements");
                dumpBaseElems(snapElems);
                dumpIssues(_generator.Outcome?.Issue);
                Assert.AreEqual(62, snapElems.Count);
                Assert.IsNull(_generator.Outcome);

                var issues = new List<OperationOutcome.IssueComponent>();
                var fullElems = await fullyExpand(snapElems, issues);
                Debug.WriteLine($"Full expansion: {fullElems.Count} elements");
                dumpBaseElems(fullElems);
                dumpIssues(issues);
                // [WMR 20181212] R4 FIXED
                // * Added elements: Meta.source, Reference.type
                // +1 Organization.meta.source
                // +1 Organization.contained.meta.source
                // +4 Organization.identifier.assigner.type (slice intro + 3 named slices)
                // +1 Organization.partOf.type
                // +1 Organization.endpoint.type
                // +8 in total
                //Assert.AreEqual(347, fullElems.Count);
                //Assert.AreEqual(355, fullElems.Count);

                // [WMR 20190211] Fixed
                // R4: snapshot now includes both "value[x]" and "valueString" constraints
                // +1 Organization.address.extension.value[x]
                // +1 Organization.address.line.extension:streetName.value[x]
                // +1 Organization.address.line.extension:houseNumber.value[x]
                // +1 Organization.address.line.extension:buildingNumberSuffix.value[x]
                // +1 Organization.address.line.extension:unitID.value[x]
                // +1 Organization.address.line.extension:additionalLocator.value[x]

                // [MV 20191216] Fixed
                // R4.0.1: snapshot only includes "value[x]" constraints not "valueString" constraints anymore
                // -1 Organization.address.line.extension.value[x]:valueString
                // -1 Organization.address.line.extension.value[x]:valueString
                // -1 Organization.address.line.extension.value[x]:valueString
                // -1 Organization.address.line.extension.value[x]:valueString
                // -1 Organization.address.line.extension.value[x]:valueString
                Assert.AreEqual(356, fullElems.Count);

                Assert.AreEqual(0, issues.Count);

                // Verify
                for (int j = 1; j < fullElems.Count; j++)
                {
                    if (isExpandableElement(fullElems[j]))
                    {
                        await verifyExpandElement(fullElems[j], fullElems, fullElems);
                    }
                }
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }
        }

        private static void beforeExpandElementHandler_DEBUG(object sender, SnapshotExpandElementEventArgs e)
        {
            Debug.Print($"[beforeExpandElementHandler_DEBUG] #{e.Element.GetHashCode()} '{e.Element.Path}' - HasChildren = {e.HasChildren} - MustExpand = {e.MustExpand}");
        }

        [TestMethod]
        public async Tasks.Task TestSnapshotRecursionChecker()
        {
            // Following structuredefinition has a recursive element type profile
            // Verify that the snapshot generator detects recursion and aborts with exception

            var sd = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyBundle");

            Assert.IsNotNull(sd);

            // dumpReferences(sd);

            bool exceptionRaised = false;
            try
            {
                var (_, expanded) = await generateSnapshotAndCompare(sd);
                dumpOutcome(_generator.Outcome);
                dumpBasePaths(expanded);
            }
            catch (Exception ex)
            {
                Debug.Print("{0}: {1}".FormatWith(ex.GetType().Name, ex.Message));
                exceptionRaised = ex is NotSupportedException;
            }
            Assert.IsTrue(exceptionRaised);
        }

        // [WMR 20170424] Add qicore-encounter.xml (STU3) as separate content file
        // Source: http://build.fhir.org/ig/cqframework/qi-core/StructureDefinition-qicore-encounter.xml.html
        [TestMethod]
        public async Tasks.Task GenerateDerivedProfileSnapshot()
        {
            // [WMR 20161005] Verify that the snapshot generator supports profiles on profiles

            // cqif-guidanceartifact profile is derived from cqif-knowledgemodule
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/cqif-guidanceartifact");
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/sdc-questionnaire");
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/qicore-goal");
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/qicore-patient");
            // var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/qicore-encounter");
            var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org/fhir/us/qicore/StructureDefinition/qicore-encounter");

            Assert.IsNotNull(sd);
            // dumpReferences(sd);

            var (_, expanded) = await generateSnapshotAndCompare(sd);

            dumpOutcome(_generator.Outcome);
            dumpBasePaths(expanded);
        }

        private void assertContainsElement(StructureDefinition sd, string path, string name = null, string elementId = null)
        {
            Assert.IsNotNull(sd);

            Assert.IsNotNull(sd.Differential);
            Assert.IsNotNull(sd.Differential.Element);
            Assert.IsTrue(sd.Differential.Element.Count > 0);

            // Verify that the differential component contains a matching element
            assertContainsElement(sd.Differential, path, name);
            assertContainsElement(sd.Snapshot, path, name, elementId);
        }

        private void assertContainsElement(IElementList elements, string path, string name = null, string elementId = null)
        {
            var label = elements is StructureDefinition.DifferentialComponent ? "differential" : "snapshot";
            Assert.IsNotNull(elements);
            var matches = elements.Element.Where(e => e.Path == path && e.SliceName == name).ToArray();
            var cnt = matches.Length;
            Assert.IsTrue(cnt > 0, $"Expected element is missing from {label} component. Path = '{path}', name = '{name}'.");
            Assert.IsTrue(cnt == 1, $"Found multiple matching elements in {label} component for Path = '{path}', name = '{name}'.");
            var elem = matches[0];
            if (_settings.GenerateElementIds && elementId != null)
            {
                Assert.AreEqual(elementId, elem.ElementId, $"Invalid elementId in {label} component. Expected = '{elementId}', actual = '{elem.ElementId}'.");
            }
        }

        private async Tasks.Task<StructureDefinition> generateSnapshot(string url, Action<StructureDefinition> preprocessor = null)
        {
            var structure = await _testResolver.FindStructureDefinitionAsync(url);
            Assert.IsNotNull(structure);
            Assert.IsTrue(structure.HasSnapshot);
            preprocessor?.Invoke(structure);
            (_, var expanded) = await generateSnapshotAndCompare(structure);
            dumpOutcome(_generator.Outcome);
            return expanded;
        }

        private static void insertElementsBefore(StructureDefinition structure, ElementDefinition insertBefore, params ElementDefinition[] inserts)
            => insertElementsBefore(structure.Differential.Element, insertBefore, inserts);

        private static void insertElementsBefore(List<ElementDefinition> elements, ElementDefinition insertBefore, params ElementDefinition[] inserts)
        {
            var idx = elements.FindIndex(e => e.Path == insertBefore.Path && e.SliceName == insertBefore.SliceName);
            Assert.AreNotEqual(-1, idx, $"Warning! insertBefore element is missing. Path = '{insertBefore.Path}', Name = '{insertBefore.SliceName}'.");
            foreach (var insert in inserts)
            {
                var idx2 = elements.FindIndex(e => e.Path == insert.Path && e.SliceName == insert.SliceName);
                Assert.AreEqual(-1, idx2, $"Warning! insert element is already present. Path = '{insert.Path}', Name = '{insert.SliceName}'.");
            }
            elements.InsertRange(idx, inserts);
        }

        private static void insertElementsBefore(StructureDefinition structure, string insertBeforePath, int elemIndex, params ElementDefinition[] inserts)
            => insertElementsBefore(structure.Differential.Element, insertBeforePath, elemIndex, inserts);

        private static void insertElementsBefore(List<ElementDefinition> elements, string insertBeforePath, int elemIndex, params ElementDefinition[] inserts)
        {
            var idx = -1;
            do
            {
                idx = elements.FindIndex(idx + 1, e => e.Path == insertBeforePath);
                Assert.AreNotEqual(-1, idx, $"Warning! insertBefore element is missing. Path = '{insertBeforePath}', Index = '{elemIndex}'.");
            } while (--elemIndex > 0);

            foreach (var insert in inserts)
            {
                var idx2 = elements.FindIndex(e => e.Path == insert.Path && e.SliceName == insert.SliceName);
                Assert.AreEqual(-1, idx2, $"Warning! insert element is already present. Path = '{insert.Path}', Name = '{insert.SliceName}'.");
            }
            elements.InsertRange(idx, inserts);

        }


        // [WMR 20170412] Fixed
        [TestMethod]
        public async Tasks.Task GeneratePatientWithExtensionsSnapshot()
        {
            // [WMR 20161005] Very complex set of examples by Chris Grenz
            // https://github.com/chrisgrenz/FHIR-Primer/blob/master/profiles/patient-extensions-profile.xml
            // Manually downgraded from FHIR v1.4.0 to v1.0.2

            StructureDefinition sd;
            ElementVerifier verifier;

            // [WMR 20170421] Chris Grenz examples define non-standard slice names, e.g. "type.value[x]"
            _settings.GenerateElementIds = true;

            // http://example.com/fhir/StructureDefinition/patient-legal-case
            // http://example.com/fhir/StructureDefinition/patient-legal-case-lead-counsel

            // [WMR 20170424] Corrected element ids

            // Verify complex extension used by patient-with-extensions profile
            // patient-research-authorization-profile.xml
            sd = await generateSnapshot(@"http://example.com/fhir/StructureDefinition/patient-research-authorization");
            verifier = new ElementVerifier(sd, _settings);
            verifier.VerifyElement("Extension.extension", null, "Extension.extension");
            verifier.VerifyElement("Extension.extension", "type", "Extension.extension:type");
            verifier.VerifyElement("Extension.extension.url", null, "Extension.extension:type.url", new FhirUri("type"));
            verifier.VerifyElement("Extension.extension", "flag", "Extension.extension:flag");
            verifier.VerifyElement("Extension.extension.url", null, "Extension.extension:flag.url", new FhirUri("flag"));
            verifier.VerifyElement("Extension.extension", "date", "Extension.extension:date");
            verifier.VerifyElement("Extension.extension.url", null, "Extension.extension:date.url", new FhirUri("date"));
            verifier.VerifyElement("Extension.url", null, null, new FhirUri(sd.Url));

            // Basic Patient profile that references a set of extensions
            // patient-extensions-profile.xml
            sd = await generateSnapshot(@"http://example.com/fhir/StructureDefinition/patient-with-extensions");
            verifier = new ElementVerifier(sd, _settings);
            verifier.VerifyElement("Patient.extension", null, "Patient.extension");
            verifier.VerifyElement("Patient.extension", "doNotCall", "Patient.extension:doNotCall");
            verifier.VerifyElement("Patient.extension", "legalCase", "Patient.extension:legalCase");

            // [WMR 20170614] Fixed; element id for type slice is based on original element name ending with "[x]"
            // verifier.VerifyElement("Patient.extension.valueBoolean", null, "Patient.extension:legalCase.valueBoolean");
            // verifier.VerifyElement("Patient.extension.valueBoolean.extension", null, "Patient.extension:legalCase.valueBoolean.extension");
            // verifier.VerifyElement("Patient.extension.valueBoolean.extension", "leadCounsel", "Patient.extension:legalCase.valueBoolean.extension:leadCounsel");
            // [WMR 20190822] Fixed; element id for type slice should contain type slice name "value[x]:valueBoolean"
            //verifier.VerifyElement("Patient.extension.valueBoolean", null, "Patient.extension:legalCase.value[x]");
            //verifier.VerifyElement("Patient.extension.valueBoolean.extension", null, "Patient.extension:legalCase.value[x].extension");
            //verifier.VerifyElement("Patient.extension.valueBoolean.extension", "leadCounsel", "Patient.extension:legalCase.value[x].extension:leadCounsel");
#if NORMALIZE_RENAMED_TYPESLICE
            verifier.VerifyElement("Patient.extension.value[x]", null, "Patient.extension:legalCase.value[x]"); // Slice entry
            verifier.VerifyElement("Patient.extension.value[x]", "valueBoolean", "Patient.extension:legalCase.value[x]:valueBoolean");
            verifier.VerifyElement("Patient.extension.value[x].extension", null, "Patient.extension:legalCase.value[x]:valueBoolean.extension");
            verifier.VerifyElement("Patient.extension.value[x].extension", "leadCounsel", "Patient.extension:legalCase.value[x]:valueBoolean.extension:leadCounsel");
#else
            verifier.VerifyElement("Patient.extension.valueBoolean", null, "Patient.extension:legalCase.value[x]:valueBoolean");
            verifier.VerifyElement("Patient.extension.valueBoolean.extension", null, "Patient.extension:legalCase.value[x]:valueBoolean.extension");
            verifier.VerifyElement("Patient.extension.valueBoolean.extension", "leadCounsel", "Patient.extension:legalCase.value[x]:valueBoolean.extension:leadCounsel");
#endif
            verifier.VerifyElement("Patient.extension", "religion", "Patient.extension:religion");
            verifier.VerifyElement("Patient.extension", "researchAuth", "Patient.extension:researchAuth");

            // Each of the following profiles is derived from the previous profile

            // patient-name-slice-profile.xml
            sd = await generateSnapshot(@"http://example.com/fhir/SD/patient-name-slice"
                , structure => insertElementsBefore(structure,
                     "Patient.name.use", 2,
                     // Add named parent slicing entry
                     new ElementDefinition() { Path = "Patient.name", SliceName = "maidenName" }
                 )
            );
            verifier = new ElementVerifier(sd, _settings);
            verifier.VerifyElement("Patient.name", null, "Patient.name");
            verifier.VerifyElement("Patient.name", "officialName", "Patient.name:officialName");
            verifier.VerifyElement("Patient.name.text", null, "Patient.name:officialName.text");
            verifier.VerifyElement("Patient.name.family", null, "Patient.name:officialName.family");
            verifier.VerifyElement("Patient.name.given", null, "Patient.name:officialName.given");
            verifier.VerifyElement("Patient.name.use", null, "Patient.name:officialName.use");
            Assert.AreEqual((verifier.CurrentElement.Fixed as Code)?.Value, "official");
            verifier.VerifyElement("Patient.name", "maidenName", "Patient.name:maidenName");
            verifier.VerifyElement("Patient.name.use", null, "Patient.name:maidenName.use");
            Assert.AreEqual((verifier.CurrentElement.Fixed as Code)?.Value, "maiden");
            verifier.VerifyElement("Patient.name.family", null, "Patient.name:maidenName.family");

            // patient-telecom-slice-profile.xml
            sd = await generateSnapshot(@"http://example.com/fhir/SD/patient-telecom-slice"
                , structure => insertElementsBefore(structure,
                     // new ElementDefinition() { Path = "Patient.telecom.system", SliceName = "workEmail.system" },
                     "Patient.telecom.system", 4,
                     // Add named parent slicing entry
                     new ElementDefinition() { Path = "Patient.telecom", SliceName = "workEmail" }
                 )
            );
            verifier = new ElementVerifier(sd, _settings);
            verifier.VerifyElement("Patient.telecom", null, "Patient.telecom");
            verifier.VerifyElement("Patient.telecom", "homePhone", "Patient.telecom:homePhone");
            verifier.VerifyElement("Patient.telecom.system", null, "Patient.telecom:homePhone.system", new Code("phone"));
            verifier.VerifyElement("Patient.telecom.use", null, "Patient.telecom:homePhone.use", new Code("home"));
            verifier.VerifyElement("Patient.telecom", "mobilePhone", "Patient.telecom:mobilePhone");
            verifier.VerifyElement("Patient.telecom.system", null, "Patient.telecom:mobilePhone.system", new Code("phone"));
            verifier.VerifyElement("Patient.telecom.use", null, "Patient.telecom:mobilePhone.use", new Code("mobile"));
            verifier.VerifyElement("Patient.telecom", "homeEmail", "Patient.telecom:homeEmail");
            verifier.VerifyElement("Patient.telecom.system", null, "Patient.telecom:homeEmail.system", new Code("email"));
            verifier.VerifyElement("Patient.telecom.use", null, "Patient.telecom:homeEmail.use", new Code("home"));
            verifier.VerifyElement("Patient.telecom", "workEmail", "Patient.telecom:workEmail");
            verifier.VerifyElement("Patient.telecom.system", null, "Patient.telecom:workEmail.system", new Code("email"));
            verifier.VerifyElement("Patient.telecom.use", null, "Patient.telecom:workEmail.use", new Code("work"));
            verifier.VerifyElement("Patient.telecom", "pager", "Patient.telecom:pager");
            verifier.VerifyElement("Patient.telecom.system", null, "Patient.telecom:pager.system", new Code("pager"));

            // Original snapshot contains constraints for both deceased[x] and deceasedDateTime - invalid!
            // Generated snapshot merges both constraints to deceasedDateTime type slice
            // patient-deceasedDatetime-slice-profile.xml
            sd = await generateSnapshot(@"http://example.com/fhir/SD/patient-deceasedDatetime-slice");
            assertContainsElement(sd.Differential, "Patient.deceased[x]");                  // Differential contains a type slice on deceased[x]
            // Assert.IsFalse(sd.Snapshot.Element.Any(e => e.Path == "Patient.deceased[x]"));  // Snapshot only contains renamed element constraint
            // assertContainsElement(sd, "Patient.deceasedDateTime", null, "Patient.deceasedDateTime");
            verifier.VerifyElement("Patient.deceased[x]", null, "Patient.deceased[x]");

            // patient-careprovider-type-slice-profile.xml
            sd = await generateSnapshot(@"http://example.com/fhir/SD/patient-careprovider-type-slice");
            verifier = new ElementVerifier(sd, _settings);
            verifier.VerifyElement("Patient.careProvider", null, "Patient.careProvider");
            verifier.VerifyElement("Patient.careProvider", "organizationCare", "Patient.careProvider:organizationCare");
            verifier.VerifyElement("Patient.careProvider", "practitionerCare", "Patient.careProvider:practitionerCare");

            // Verify re-slicing
            // patient-careprovider-type-reslice-profile.xml
            sd = await generateSnapshot(@"http://example.com/fhir/SD/patient-careprovider-type-reslice");
            verifier = new ElementVerifier(sd, _settings);
            verifier.VerifyElement("Patient.careProvider", null, "Patient.careProvider");
            verifier.VerifyElement("Patient.careProvider", "organizationCare", "Patient.careProvider:organizationCare");
            verifier.VerifyElement("Patient.careProvider", "organizationCare/teamCare", "Patient.careProvider:organizationCare/teamCare");
            verifier.VerifyElement("Patient.careProvider", "practitionerCare", "Patient.careProvider:practitionerCare");

            // Identifier Datatype profile
            // patient-mrn-id-profile.xml
            sd = await generateSnapshot(@"http://example.com/fhir/SD/patient-mrn-id");
            verifier = new ElementVerifier(sd, _settings);
            verifier.VerifyElement("Identifier", null, "Identifier");
            verifier.VerifyElement("Identifier.system", null, "Identifier.system", new FhirUri(@"http://example.com/fhir/localsystems/PATIENT-ID-MRN"));

            // Verify inline re-slicing
            // Profile slices identifier and also re-slices the "mrn" slice
            // patient-identifier-profile-slice-profile.xml
            sd = await generateSnapshot(@"http://example.com/fhir/SD/patient-slice-by-profile"
                , structure => insertElementsBefore(structure,
                     "Patient.identifier.use", 1,
                     // Add named parent reslicing entry
                     new ElementDefinition() { Path = "Patient.identifier", SliceName = "mrn/officialMRN" }
                 )
            );
            verifier = new ElementVerifier(sd, _settings);
            verifier.VerifyElement("Patient.identifier", null, "Patient.identifier");
            verifier.VerifyElement("Patient.identifier", "mrn", "Patient.identifier:mrn");
            verifier.VerifyElement("Patient.identifier", "mrn/officialMRN", "Patient.identifier:mrn/officialMRN");
            verifier.VerifyElement("Patient.identifier.use", null, "Patient.identifier:mrn/officialMRN.use", new Code("official"));
            verifier.VerifyElement("Patient.identifier", "mdmId", "Patient.identifier:mdmId");

            // Verify constraints on named slice in base profile
            // patient-identifier-slice-extension-profile.xml
            sd = await generateSnapshot(@"http://example.com/fhir/SD/patient-identifier-subslice"
                , structure => insertElementsBefore(structure,
                     "Patient.identifier.extension", 1,
                     // Add named parent reslicing entry
                     new ElementDefinition() { Path = "Patient.identifier", SliceName = "mrn" }
                 )
            );
            verifier = new ElementVerifier(sd, _settings);
            verifier.VerifyElement("Patient.identifier", null, "Patient.identifier");
            verifier.AssertSlicing("system", ElementDefinition.SlicingRules.Open, null);
            verifier.VerifyElement("Patient.identifier", "mrn", "Patient.identifier:mrn");
            verifier.AssertSlicing("use", ElementDefinition.SlicingRules.Open, null);
            verifier.VerifyElement("Patient.identifier.extension", null, "Patient.identifier:mrn.extension");
            verifier.VerifyElement("Patient.identifier.extension", "issuingSite", "Patient.identifier:mrn.extension:issuingSite");
            verifier.VerifyElement("Patient.identifier.use", null, "Patient.identifier:mrn.use");
            verifier.VerifyElement("Patient.identifier.type", null, "Patient.identifier:mrn.type");
            verifier.VerifyElement("Patient.identifier.system", null, "Patient.identifier:mrn.system", new FhirUri(@"http://example.com/fhir/localsystems/PATIENT-ID-MRN"));
            verifier.VerifyElement("Patient.identifier.value", null, "Patient.identifier:mrn.value");
            verifier.VerifyElement("Patient.identifier.period", null, "Patient.identifier:mrn.period");
            verifier.VerifyElement("Patient.identifier.assigner", null, "Patient.identifier:mrn.assigner");
            verifier.VerifyElement("Patient.identifier", "mrn/officialMRN", "Patient.identifier:mrn/officialMRN");
            verifier.VerifyElement("Patient.identifier", "mdmId", "Patient.identifier:mdmId");

            // Verify extension re-slice
            // patient-research-auth-reslice-profile.xml
            sd = await generateSnapshot(@"http://example.com/fhir/SD/patient-research-auth-reslice"
                , structure => insertElementsBefore(structure,
                     // new ElementDefinition() { Path = "Patient.extension.extension.value[x]", SliceName = "researchAuth/grandfatheredResAuth.type.value[x]" },
                     "Patient.extension.extension.value[x]", 1,
                     // Add named parent reslicing entry
                     new ElementDefinition() { Path = "Patient.extension", SliceName = "researchAuth/grandfatheredResAuth" },
                     new ElementDefinition() { Path = "Patient.extension.extension", SliceName = "type" }
                     // new ElementDefinition() { Path = "Patient.extension.extension", Name = "researchAuth/grandfatheredResAuth.type" }
                 )
            );
            verifier = new ElementVerifier(sd, _settings);

            verifier.VerifyElement("Patient.extension", null, "Patient.extension");
            verifier.VerifyElement("Patient.extension", "doNotCall", "Patient.extension:doNotCall");
            verifier.VerifyElement("Patient.extension", "legalCase", "Patient.extension:legalCase");

            // [WMR 20170614] Fixed; element id for type slices is based on original element name ending with "[x]"
            // verifier.VerifyElement("Patient.extension.valueBoolean", null, "Patient.extension:legalCase.valueBoolean");
            // verifier.VerifyElement("Patient.extension.valueBoolean.extension", null, "Patient.extension:legalCase.valueBoolean.extension");
            // verifier.VerifyElement("Patient.extension.valueBoolean.extension", null, "Patient.extension:legalCase.valueBoolean.extension:leadCounsel");
            // [WMR 20190822] Fixed; element id for type slice should contain type slice name "value[x]:valueBoolean"
            //verifier.VerifyElement("Patient.extension.valueBoolean", null, "Patient.extension:legalCase.value[x]");
            //verifier.VerifyElement("Patient.extension.valueBoolean.extension", null, "Patient.extension:legalCase.value[x].extension");
            //verifier.VerifyElement("Patient.extension.valueBoolean.extension", null, "Patient.extension:legalCase.value[x].extension:leadCounsel");
#if NORMALIZE_RENAMED_TYPESLICE
            verifier.VerifyElement("Patient.extension.value[x]", null, "Patient.extension:legalCase.value[x]"); // Slice entry
            verifier.VerifyElement("Patient.extension.value[x]", "valueBoolean", "Patient.extension:legalCase.value[x]:valueBoolean");
            verifier.VerifyElement("Patient.extension.value[x].extension", null, "Patient.extension:legalCase.value[x]:valueBoolean.extension");
            verifier.VerifyElement("Patient.extension.value[x].extension", "leadCounsel", "Patient.extension:legalCase.value[x]:valueBoolean.extension:leadCounsel");
#else
            verifier.VerifyElement("Patient.extension.valueBoolean", null, "Patient.extension:legalCase.value[x]:valueBoolean");
            verifier.VerifyElement("Patient.extension.valueBoolean.extension", null, "Patient.extension:legalCase.value[x]:valueBoolean.extension");
            verifier.VerifyElement("Patient.extension.valueBoolean.extension", null, "Patient.extension:legalCase.value[x]:valueBoolean.extension:leadCounsel");
#endif

            verifier.VerifyElement("Patient.extension", "religion", "Patient.extension:religion");
            verifier.VerifyElement("Patient.extension", "researchAuth", "Patient.extension:researchAuth");
            // Note: in the original snapshot, the "researchAuth" complex extension slice is fully expanded (child extensions: type, flag, date)
            // However this is not necessary, as there are no child constraints on the extension

            // [WMR 20161216] TODO: Merge slicing entry
            verifier.AssertSlicing("type.value[x]", ElementDefinition.SlicingRules.Open, null);

            // [WMR 20161208] TODO...

            // "researchAuth/grandfatheredResAuth" represents a reslice of the base extension "researchAuth" (0...*)
            verifier.VerifyElement("Patient.extension", "researchAuth/grandfatheredResAuth", "Patient.extension:researchAuth/grandfatheredResAuth");

            // [WMR 20161216] TODO: Merge slicing entry
            verifier.VerifyElement("Patient.extension.extension", null, "Patient.extension:researchAuth/grandfatheredResAuth.extension");
            // [WMR 20170412] Slicing component is inherited from Extension.extension core element definition
            // STU3: Defined as { type = "value", path = "url", ordered = null }
            verifier.AssertSlicing("url", ElementDefinition.SlicingRules.Open, null);

            // The reslice "researchAuth/grandfatheredResAuth" has a child element constraint on "type.value[x]"
            // Therefore the complex extension is fully expanded (child extensions: type, flag, date)
            verifier.VerifyElement("Patient.extension.extension", "type", "Patient.extension:researchAuth/grandfatheredResAuth.extension:type");
            verifier.VerifyElement("Patient.extension.extension.url", null, "Patient.extension:researchAuth/grandfatheredResAuth.extension:type.url", new FhirUri("type"));
            // Child constraints on "type.value[x]" merged from differential
            verifier.VerifyElement("Patient.extension.extension.value[x]", null, "Patient.extension:researchAuth/grandfatheredResAuth.extension:type.value[x]");
            verifier.VerifyElement("Patient.extension.extension", "flag", "Patient.extension:researchAuth/grandfatheredResAuth.extension:flag");
            verifier.VerifyElement("Patient.extension.extension.url", null, "Patient.extension:researchAuth/grandfatheredResAuth.extension:flag.url", new FhirUri("flag"));
            verifier.VerifyElement("Patient.extension.extension", "date", "Patient.extension:researchAuth/grandfatheredResAuth.extension:date");
            verifier.VerifyElement("Patient.extension.extension.url", null, "Patient.extension:researchAuth/grandfatheredResAuth.extension:date.url", new FhirUri("date"));
            verifier.VerifyElement("Patient.extension.url", null, "Patient.extension:researchAuth/grandfatheredResAuth.url", new FhirUri(@"http://example.com/fhir/StructureDefinition/patient-research-authorization"));
            verifier.VerifyElement("Patient.extension.value[x]", null, "Patient.extension:researchAuth/grandfatheredResAuth.value[x]");

            // Slices inherited from base profile with url http://example.com/fhir/SD/patient-identifier-subslice
            verifier.VerifyElement("Patient.identifier", null, "Patient.identifier");
            verifier.AssertSlicing("system", ElementDefinition.SlicingRules.Open, null);
            verifier.VerifyElement("Patient.identifier", "mrn", "Patient.identifier:mrn");
            verifier.AssertSlicing("use", ElementDefinition.SlicingRules.Open, null);
            verifier.VerifyElement("Patient.identifier.extension", null, "Patient.identifier:mrn.extension");
            verifier.VerifyElement("Patient.identifier.extension", null, "Patient.identifier:mrn.extension:issuingSite");
            verifier.VerifyElement("Patient.identifier.use", null, "Patient.identifier:mrn.use");
            verifier.VerifyElement("Patient.identifier.type", null, "Patient.identifier:mrn.type");
            verifier.VerifyElement("Patient.identifier.system", null, "Patient.identifier:mrn.system", new FhirUri(@"http://example.com/fhir/localsystems/PATIENT-ID-MRN"));
            verifier.VerifyElement("Patient.identifier.value", null, "Patient.identifier:mrn.value");
            verifier.VerifyElement("Patient.identifier.period", null, "Patient.identifier:mrn.period");
            verifier.VerifyElement("Patient.identifier.assigner", null, "Patient.identifier:mrn.assigner");
            verifier.VerifyElement("Patient.identifier", "mrn/officialMRN", "Patient.identifier:mrn/officialMRN");
            verifier.VerifyElement("Patient.identifier", "mdmId", "Patient.identifier:mdmId");
        }

        [TestMethod]
        public async Tasks.Task GenerateSnapshotExpandExternalProfile()
        {
            // Profile MyLocation references extension MyLocationExtension
            // MyLocationExtension extension profile does not have a snapshot component => expand on demand

            var sd = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyLocation");
            Assert.IsNotNull(sd);
            Assert.IsNotNull(sd.Snapshot);

            var extensionElements = sd.Differential.Element.Where(e => e.IsExtension());
            Assert.IsNotNull(extensionElements);
            Assert.AreEqual(2, extensionElements.Count()); // Extension slicing entry + first extension definition
            var extensionElement = extensionElements.Skip(1).FirstOrDefault();
            var extensionType = extensionElement.Type.FirstOrDefault();
            Assert.IsNotNull(extensionType);
            Assert.AreEqual(FHIRAllTypes.Extension.GetLiteral(), extensionType.Code);
            Assert.IsNotNull(extensionType.Profile);
            var extDefUrl = extensionType.Profile.FirstOrDefault();
            Assert.AreEqual(@"http://example.org/fhir/StructureDefinition/MyLocationExtension", extDefUrl);
            var ext = await _testResolver.FindStructureDefinitionAsync(extDefUrl);
            Assert.IsNotNull(ext);
            Assert.IsNull(ext.Snapshot);

            // dumpReferences(sd);

            var (_, expanded) = await generateSnapshotAndCompare(sd);

            dumpOutcome(_generator.Outcome);
            dumpBasePaths(expanded);
        }

        [TestMethod]
        public async Tasks.Task GenerateSnapshotIgnoreMissingExternalProfile()
        {
            // [WMR 20161005] Verify that the snapshot generator gracefully handles unresolved external profile references
            // This should generate a partial snapshot and OperationOutcome Issues for each missing dependency.

            var sd = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyObservation");
            Assert.IsNotNull(sd);

            dumpReferences(sd, true);

            // Explicitly disable expansion of external snapshots
            var settings = new SnapshotGeneratorSettings(_settings)
            {
                GenerateSnapshotForExternalProfiles = false
            };
            _generator = new SnapshotGenerator(_testResolver, settings);
            _ = await generateSnapshotAndCompare(sd);

            var outcome = _generator.Outcome;
            dumpOutcome(outcome);

            Assert.IsNotNull(outcome);
            Assert.AreEqual(3, outcome.Issue.Count);

            assertIssue(outcome.Issue[0], Issue.UNAVAILABLE_REFERENCED_PROFILE, "http://example.org/fhir/StructureDefinition/MyMissingExtension");
            // Note: the extension reference to MyExtensionNoSnapshot should not generate an Issue,
            // as the profile only needs to merge the extension definition root element (no full expansion)
            assertIssue(outcome.Issue[1], Issue.UNAVAILABLE_REFERENCED_PROFILE, "http://example.org/fhir/StructureDefinition/MyIdentifier");
            assertIssue(outcome.Issue[2], Issue.UNAVAILABLE_REFERENCED_PROFILE, "http://example.org/fhir/StructureDefinition/MyCodeableConcept");
        }

        private static void assertIssue(OperationOutcome.IssueComponent issue, Issue expected, string diagnostics = null, params string[] location)
        {
            Assert.IsNotNull(issue);
            Assert.AreEqual(expected.Type, issue.Code);
            Assert.AreEqual(expected.Severity, issue.Severity);
            Assert.AreEqual(expected.Code.ToString(), issue.Details.Coding[0].Code);
            Assert.IsNotNull(issue.Extension);
            if (diagnostics != null)
            {
                Assert.AreEqual(diagnostics, issue.Diagnostics);
            }
            if (location != null && location.Length > 0)
            {
                Assert.IsTrue(location.SequenceEqual(issue.Location));
            }
        }

        // [WMR 20160721] Following profiles are not yet handled (TODO)
        //      private readonly string[] skippedProfiles =
        //      {
        //	// Differential defines constraint on MedicationOrder.reason[x]
        //	// Snapshot renames this element to MedicationOrder.reasonCodeableConcept - is this mandatory?
        //	// @"http://hl7.org/fhir/StructureDefinition/gao-medicationorder",
        //};
        [TestMethod, Ignore]
        public async Tasks.Task GenerateSnapshot()
        {
            var sw = new Stopwatch();
            int count = 0;
            _source.Reset();
            sw.Start();

            foreach (var original in findConstraintStrucDefs()
            // [WMR 20160721] Skip invalid profiles
            // .Where(sd => !skippedProfiles.Contains(sd.Url))
            )
            {
                // nothing to test, original does not have a snapshot
                if (original.Snapshot == null) continue;

                Debug.WriteLine("Generating Snapshot for " + original.Url);

                await generateSnapshotAndCompare(original);
                count++;
            }

            sw.Stop();
            _source.ShowDuration(count, sw.Elapsed);
        }

        private async Tasks.Task<StructureDefinition> generateSnapshot(StructureDefinition original)
        {
            if (_generator == null)
            {
                _generator = new SnapshotGenerator(_testResolver, _settings);
            }

            var expanded = (StructureDefinition)original.DeepCopy();
            Assert.IsTrue(original.IsExactly(expanded));

            await _generator.UpdateAsync(expanded);

            return expanded;
        }

        private async Tasks.Task<(bool, StructureDefinition expanded)> generateSnapshotAndCompare(StructureDefinition original)
        {
            var expanded = await generateSnapshot(original);

            var areEqual = original.IsExactly(expanded);

            // [WMR 20160803] Always save output to separate file, convenient for debugging
            // if (!areEqual)
            // {
            var tempPath = Path.GetTempPath();
            var xmlSer = new FhirXmlSerializer();
            await File.WriteAllTextAsync(Path.Combine(tempPath, "snapshotgen-source.xml"), await xmlSer.SerializeToStringAsync(original));
            await File.WriteAllTextAsync(Path.Combine(tempPath, "snapshotgen-dest.xml"), await xmlSer.SerializeToStringAsync(expanded));
            // }

            // Assert.IsTrue(areEqual);
            Debug.WriteLineIf(original.HasSnapshot && !areEqual, "WARNING: '{0}' Expansion ({1} elements) is not equal to original ({2} elements)!".FormatWith(
                original.Name, original.HasSnapshot ? original.Snapshot.Element.Count : 0, expanded.HasSnapshot ? expanded.Snapshot.Element.Count : 0)
            );

            return (areEqual, expanded);
        }

        private IEnumerable<StructureDefinition> findConstraintStrucDefs()
        {
#if true
            if (_source.Source is DirectorySource dirSource)
            {
                //var summaries = dirSource.ListSummaries(ResourceType.StructureDefinition);
                //summaries = summaries.Where(s => Path.GetFileNameWithoutExtension(s.Origin) == "profiles-others");
                var path = Path.GetFullPath(@"TestData\snapshot-test\WMR\profiles-others.xml");
                var summaries = dirSource.ListSummaries(ResourceType.StructureDefinition).FromFile(path);
                foreach (var summary in summaries)
                {
                    var canonical = summary.GetConformanceCanonicalUrl();
                    if (canonical != null)
                    {
                        yield return _source.ResolveByCanonicalUri(canonical) as StructureDefinition;
                    }
                }
            }
#else
            var testSDs = _source.FindAll<StructureDefinition>();

            foreach (var testSD in testSDs)
            {
                // var sdInfo = testSD.Annotation<OriginAnnotation>();
                // [WMR 20160721] Select all profiles in profiles-others.xml
                // var fileName = Path.GetFileNameWithoutExtension(sdInfo.Origin);
                var fileName = Path.GetFileNameWithoutExtension(testSD.GetOrigin());
                if (fileName == "profiles-others")
                {
                    //var sd = await _testResolver.FindStructureDefinitionAsync(sdInfo.Canonical);

                    //if (sd == null) throw new InvalidOperationException(("Source listed canonical url {0} [source {1}], " +
                    //    "but could not get structure definition by that url later on!").FormatWith(sdInfo.Canonical, sdInfo.Origin));

                    if (testSD.IsConstraint || testSD.IsExtension)
                        yield return testSD;
                }
            }
#endif
        }

        // Unit tests for DifferentialTreeConstructor

        [TestMethod]
        public void TestDifferentialTree()
        {
            var e = new List<ElementDefinition>
            {
                new ElementDefinition() { Path = "A.B.C1" },
                new ElementDefinition() { Path = "A.B.C1", SliceName = "C1-A" }, // First slice of A.B.C1
                new ElementDefinition() { Path = "A.B.C2" },
                new ElementDefinition() { Path = "A.B", SliceName = "B-A" }, // First slice of A.B
                new ElementDefinition() { Path = "A.B.C1.D" },
                new ElementDefinition() { Path = "A.D.F" }
            };

            var tree = DifferentialTreeConstructor.MakeTree(e);
            Assert.IsNotNull(tree);

            var nav = new ElementDefinitionNavigator(tree);
            Assert.AreEqual(10, nav.Count);

            Assert.IsTrue(nav.MoveToChild("A"));
            Assert.IsTrue(nav.MoveToChild("B"));
            Assert.IsTrue(nav.MoveToChild("C1"));
            Assert.IsTrue(nav.MoveToNext("C1"));
            Assert.IsTrue(nav.MoveToNext("C2"));

            Assert.IsTrue(nav.MoveToParent());  // 1st A.B
            Assert.IsTrue(nav.MoveToNext() && nav.Path == "A.B");  // (now) 2nd A.B
            Assert.IsTrue(nav.MoveToChild("C1"));
            Assert.IsTrue(nav.MoveToChild("D"));

            Assert.IsTrue(nav.MoveToParent());  // A.B.C1
            Assert.IsTrue(nav.MoveToParent());  // A.B (2nd)
            Assert.IsTrue(nav.MoveToNext() && nav.Path == "A.D");
            Assert.IsTrue(nav.MoveToChild("F"));
        }

        [TestMethod]
        public void TestDifferentialTreeMultipleRoots()
        {
            var elements = new List<ElementDefinition>
            {
                new ElementDefinition() { Path = "Patient.identifier" },
                new ElementDefinition() { Path = "Patient" }
            };

            bool exceptionRaised = false;
            try
            {
                var tree = DifferentialTreeConstructor.MakeTree(elements);
            }
            catch (InvalidOperationException ex)
            {
                Debug.Print(ex.Message);
                exceptionRaised = true;
            }
            Assert.IsTrue(exceptionRaised);
        }

        // [WMR 20161012] Advanced unit test for DifferentialTreeConstructor with resliced input
        [TestMethod]
        public void TestDifferentialTreeForReslice()
        {
            var elements = new List<ElementDefinition>
            {
                new ElementDefinition() { Path = "Patient.identifier" },
                new ElementDefinition() { Path = "Patient.identifier", SliceName = "A" },
                new ElementDefinition() { Path = "Patient.identifier.use" },
                new ElementDefinition() { Path = "Patient.identifier", SliceName = "B/1" },
                new ElementDefinition() { Path = "Patient.identifier.type" },
                new ElementDefinition() { Path = "Patient.identifier", SliceName = "B/2" },
                new ElementDefinition() { Path = "Patient.identifier.period.start" },
                new ElementDefinition() { Path = "Patient.identifier", SliceName = "C/1" }
            };

            var tree = DifferentialTreeConstructor.MakeTree(elements);
            Assert.IsNotNull(tree);
            Debug.Print(string.Join(Environment.NewLine, tree.Select(e => $"{e.Path} : '{e.SliceName}'")));

            Assert.AreEqual(10, tree.Count);
            var verifier = new ElementVerifier(tree, _settings);

            verifier.VerifyElement("Patient");                      // Added: root element
            verifier.VerifyElement("Patient.identifier");
            verifier.VerifyElement("Patient.identifier", "A");
            verifier.VerifyElement("Patient.identifier.use");
            verifier.VerifyElement("Patient.identifier", "B/1");
            verifier.VerifyElement("Patient.identifier.type");
            verifier.VerifyElement("Patient.identifier", "B/2");
            verifier.VerifyElement("Patient.identifier.period");    // Added: parent element
            verifier.VerifyElement("Patient.identifier.period.start");
            verifier.VerifyElement("Patient.identifier", "C/1");
        }

#if false
        [TestMethod]
        public async Tasks.Task DebugDifferentialTree()
        {
            var sd = await _testResolver.FindStructureDefinitionAsync(@"http://example.com/fhir/SD/patient-research-auth-reslice");
            Assert.IsNotNull(sd);
            var tree = sd.Differential.MakeTree();
            Assert.IsNotNull(tree);
            Debug.Print(string.Join(Environment.NewLine, tree.Select(e => $"{e.Path} : '{e.SliceName}'")));
        }
#endif

        // [WMR 20160802] Unit tests for SnapshotGenerator.ExpandElement

        // [WMR 20161005] internal expandElement method is no longer unit-testable; uninitialized recursion stack causes exceptions

        //[TestMethod]
        //public async Tasks.Task TestExpandChild()
        //{
        //    var sd = await _testResolver.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.Questionnaire);
        //    Assert.IsNotNull(sd);
        //    Assert.IsNotNull(sd.Snapshot);
        //    var nav = new ElementDefinitionNavigator(sd.Snapshot.Element);
        //
        //    var generator = new SnapshotGenerator(_testResolver, SnapshotGeneratorSettings.Default);
        //
        //    nav.JumpToFirst("Questionnaire.telecom");
        //    Assert.IsTrue(generator.expandElement(nav));
        //    Assert.IsTrue(nav.MoveToChild("period"), "Did not move into complex datatype ContactPoint");
        //
        //    nav.JumpToFirst("Questionnaire.group");
        //    Assert.IsTrue(generator.expandElement(nav));
        //    Assert.IsTrue(nav.MoveToChild("title"), "Did not move into internally defined backbone element Group");
        //}

        [TestMethod]
        public async Tasks.Task TestExpandElement_PatientIdentifier()
        {
            await testExpandElement(@"http://hl7.org/fhir/StructureDefinition/Patient", "Patient.identifier");
        }

        [TestMethod]
        public async Tasks.Task TestExpandElement_PatientName()
        {
            await testExpandElement(@"http://hl7.org/fhir/StructureDefinition/Patient", "Patient.name");
        }

        [TestMethod]
        public async Tasks.Task TestExpandElement_QuestionnaireItem()
        {
            // Validate name reference expansion
            await testExpandElement(@"http://hl7.org/fhir/StructureDefinition/Questionnaire", "Questionnaire.item");
        }

        [TestMethod]
        public async Tasks.Task TestExpandElement_QuestionnaireItemItem()
        {
            // Validate name reference expansion
            await testExpandElement(@"http://hl7.org/fhir/StructureDefinition/Questionnaire", "Questionnaire.item.item");
        }

        [TestMethod]
        public async Tasks.Task TestExpandElement_Slice()
        {
            // Resolve lipid profile from profile-others.xml
            var sd = await _testResolver.FindStructureDefinitionAsync("http://hl7.org/fhir/StructureDefinition/lipidprofile");
            Assert.IsNotNull(sd);
            Assert.IsNotNull(sd.Snapshot);

            // DiagnosticReport.result is sliced
            var nav = new ElementDefinitionNavigator(sd.Snapshot.Element);

            // [WMR 20170711] Fix non-standard element id's in source (capitalization)
            // Standardized element ids are preferred, but not mandatory; so the profile is not invalid
            // Nonetheless fix this first, so we can call common assertion methods
            // [WMR 20181212] R4 - Update slice names
            var elem = sd.Snapshot.Element.FirstOrDefault(e => e.ElementId == "DiagnosticReport.result:Cholesterol");
            Assert.IsNotNull(elem);
            elem.ElementId = elem.Path + ElementIdGenerator.ElementIdSliceNameDelimiter + elem.SliceName;
            Assert.AreEqual("DiagnosticReport.result:Cholesterol", elem.ElementId);
            elem = sd.Snapshot.Element.FirstOrDefault(e => e.ElementId == "DiagnosticReport.result:Triglyceride");
            elem.ElementId = elem.Path + ElementIdGenerator.ElementIdSliceNameDelimiter + elem.SliceName;
            Assert.IsNotNull(elem);
            elem.ElementId = elem.Path + ElementIdGenerator.ElementIdSliceNameDelimiter + elem.SliceName;
            Assert.AreEqual("DiagnosticReport.result:Triglyceride", elem.ElementId);

            // Move to slicing entry
            nav.JumpToFirst("DiagnosticReport.result");
            Assert.IsNotNull(nav.Current.Slicing);

            // Move to first (named) slice
            nav.MoveToNext();
            Assert.AreEqual(nav.Path, "DiagnosticReport.result");
            Assert.IsNotNull(nav.Current.SliceName);

            await testExpandElement(sd, nav.Current);
        }

        private async Tasks.Task testExpandElement(string srcProfileUrl, string expandElemPath)
        {
            // Prepare...
            var sd = await _testResolver.FindStructureDefinitionAsync(srcProfileUrl);
            Assert.IsNotNull(sd);
            Assert.IsNotNull(sd.Snapshot);

            var elems = sd.Snapshot.Element;
            Assert.IsNotNull(elems);

            Debug.WriteLine("Input:");
            Debug.Indent();
            Debug.WriteLine(string.Join(Environment.NewLine, elems.Where(e => e.Path.StartsWith(expandElemPath)).Select(e => e.Path)));
            Debug.Unindent();

            var elem = elems.FirstOrDefault(e => e.Path == expandElemPath);
            await testExpandElement(sd, elem);
        }

        private async Tasks.Task testExpandElement(StructureDefinition sd, ElementDefinition elem)
        {
            Assert.IsNotNull(elem);
            var elems = sd.Snapshot.Element;
            Assert.IsTrue(elems.Contains(elem));

            // Test...
            _generator = new SnapshotGenerator(_testResolver, _settings);

            // [WMR 20170614] NEW: ExpandElement should maintain the existing element ID...!
            var orgId = elem.ElementId;

            var result = await _generator.ExpandElementAsync(elems, elem);

            dumpOutcome(_generator.Outcome);
            Assert.IsNull(_generator.Outcome);

            Assert.AreEqual(orgId, elem.ElementId);

            // Verify results
            await verifyExpandElement(elem, elems, result);
        }

        private async Tasks.Task verifyExpandElement(ElementDefinition elem, IList<ElementDefinition> elems, IList<ElementDefinition> result)
        {
            var expandElemPath = elem.Path;

            // Debug.WriteLine("\r\nOutput:");
            // Debug.WriteLine(string.Join(Environment.NewLine, result.Where(e => e.Path.StartsWith(expandElemPath)).Select(e => e.Path)));

            Assert.IsNotNull(elem.Type);
            var elemType = elem.Type.FirstOrDefault();
            var nameRef = elem.ContentReference;
            if (elemType != null)
            {
                // Validate type profile expansion
                var elemTypeCode = elemType.Code;
                Assert.IsNotNull(elemTypeCode);

                var elemProfile = elemType.Profile.FirstOrDefault();
                var sdType = elemProfile != null && elemTypeCode != FHIRAllTypes.Reference.GetLiteral()
                    ? await _testResolver.FindStructureDefinitionAsync(elemProfile)
                    : await _testResolver.FindStructureDefinitionForCoreTypeAsync(elemTypeCode);

                // [WMR 20170220] External type profile may not be available
                // Assert.IsNotNull(sdType);
                if (sdType != null)
                {
                    Assert.IsNotNull(sdType.Snapshot);
                    Assert.IsNotNull(sdType.Snapshot.Element);
                    Assert.IsTrue(sdType.Snapshot.Element.Count > 0);

                    // Debug.WriteLine("\r\nType:");
                    // Debug.WriteLine(string.Join(Environment.NewLine, sdType.Snapshot.Element.Select(e => e.Path)));

                    sdType.Snapshot.Rebase(expandElemPath);
                    var typeElems = sdType.Snapshot.Element;

                    var nav = new ElementDefinitionNavigator(result);
                    //Assert.IsTrue(result.Count == elems.Count + typeElems.Count - 1);
                    //if (elem.Name == null)
                    //{
                    //    Assert.IsTrue(result.Where(e => e.Path.StartsWith(expandElemPath)).Count() == typeElems.Count);
                    //}
                    //else
                    if (elem.ContentReference != null)
                    {
                        // Name reference (not a slice)
                        Assert.IsTrue(nav.JumpToNameReference(elem.ContentReference));
                        var cnt = 1;
                        Assert.IsTrue(nav.MoveToFirstChild());
                        do
                        {
                            Assert.AreEqual(typeElems[cnt++].Path, nav.Path);
                        } while (nav.MoveToNext());
                        Assert.AreEqual(typeElems.Count, cnt);
                    }

                    nav.Reset();
                    Assert.IsTrue(nav.MoveTo(elem));

#if HACK_STU3_RECURSION
                    if (!isExpandableElement(elem))
                    {
                        Assert.IsFalse(nav.MoveToFirstChild());
                        return;
                    }
#endif

                    Assert.IsTrue(nav.MoveToFirstChild());
                    var typeNav = new ElementDefinitionNavigator(typeElems);
                    Assert.IsTrue(typeNav.MoveTo(typeNav.Elements[0]));
                    Assert.IsTrue(typeNav.MoveToFirstChild());
                    do
                    {
                        var path = typeNav.Path;
                        Assert.IsTrue(nav.Path.EndsWith(path, StringComparison.OrdinalIgnoreCase));
                        if (!nav.MoveToNext())
                        {
                            //Debug.Assert(!typeNav.MoveToNext());
                            Assert.IsFalse(typeNav.MoveToNext());
                            break;
                        }
                        // [WMR 20170412] Backbone elements can introduce additional child elements
                        if (!typeNav.MoveToNext())
                        {
                            Assert.AreEqual(FHIRAllTypes.BackboneElement.GetLiteral(), elemTypeCode);
                            break;
                        }

                    } while (true);
                }


            }
            else if (nameRef != null)
            {
                // Validate name reference expansion
                var nav = new ElementDefinitionNavigator(elems);
                Assert.IsTrue(nav.JumpToNameReference(nameRef));
                var prefix = nav.Path;
                Assert.IsTrue(nav.MoveToFirstChild());
                var pos = result.IndexOf(elem);

                Debug.WriteLine("\r\nName Reference:");
                Debug.Indent();
                // [WMR 20170412] Also handle grand children
                var srcPos = nav.OrdinalPosition.Value;
                var cnt = nav.Elements.Count;
                do
                {
                    Debug.WriteLine(nav.Path);
                    var srcPath = elems[srcPos++].Path.Substring(prefix.Length);
                    var tgtPath = result[++pos].Path.Substring(expandElemPath.Length);
                    Assert.AreEqual(srcPath, tgtPath);
                } while (srcPos < cnt);
                Debug.Unindent();
            }
        }

        // [WMR 20160722] For debugging purposes
        [Conditional("DEBUG")]
        private void dumpReferences(StructureDefinition sd, bool differential = false)
        {
            if (sd != null)
            {
                Debug.WriteLine("References for StructureDefinition '{0}' ('{1}')".FormatWith(sd.Name, sd.Url));
                Debug.WriteLine("BaseDefinition = '{0}'".FormatWith(sd.BaseDefinition));

                // FhirClient client = new FhirClient("http://fhir2.healthintersections.com.au/open/");
                // var folderPath = Path.Combine(Directory.GetCurrentDirectory(), @"TestData\snapshot-test\download");
                // if (!Directory.Exists(folderPath)) { Directory.CreateDirectory(folderPath); }

                var component = differential ? sd.Differential.Element : sd.Snapshot.Element;
                var profiles = enumerateDistinctTypeProfiles(component);

                Debug.Indent();
                foreach (var profile in profiles)
                {
                    Debug.WriteLine(profile);

                    // How to determine the original filename?
                    //try
                    //{
                    //    var xml = client.Get(profile);
                    //    var filePath = Path.Combine()
                    //    File.WriteAllText(folderPath, )
                    //}
                    //catch (Exception ex)
                    //{
                    //    Debug.WriteLine(ex.Message);
                    //}
                }
                Debug.Unindent();
            }
        }

        private static IEnumerable<string> enumerateDistinctTypeProfiles(IList<ElementDefinition> elements)
        {
            return elements.SelectMany(e => e.Type).SelectMany(t => t.Profile).Distinct();
        }

        private static string formatElementPathName(ElementDefinition elem) =>
            elem == null
                ? null
                : !string.IsNullOrEmpty(elem.SliceName) ?
                   $"{elem.Path}:{elem.SliceName}"
                    : elem.Path;

        [Conditional("DEBUG")]
        private static void dumpBaseElems(IEnumerable<ElementDefinition> elements)
        {
            Debug.Print(string.Join(Environment.NewLine,
                elements.Select(e =>
                {
                    var bea = e.Annotation<BaseDefAnnotation>();
                    var be = bea?.BaseElementDefinition;
                    //return "  #{0,-8} {1} '{2}' - {3} => #{4,-8} {5} '{6}' - {7}"
                    //    .FormatWith(
                    //        e.GetHashCode(),
                    //        e.Path,
                    //        e.Name,
                    //        e?.Base?.Path,
                    //        (int?)be?.GetHashCode(),
                    //        be?.Path,
                    //        be?.Name,
                    //        be?.Base?.Path
                    //    );

                    return be != null ?
                        $"  {formatElementPathName(e)} | {e.Base?.Path} <== {formatElementPathName(be)} | {be.Base?.Path}"
                      : $"  {formatElementPathName(e)} | {e.Base?.Path}";
                })
            ));
        }

        [Conditional("DEBUG")]
        private void dumpBasePaths(StructureDefinition sd)
        {
            if (sd != null && sd.Snapshot != null)
            {
                Debug.WriteLine("StructureDefinition '{0}' ('{1}')".FormatWith(sd.Name, sd.Url));
                Debug.WriteLine("BaseDefiniton = '{0}'".FormatWith(sd.BaseDefinition));
                // Debug.Indent();
                Debug.Print("Element.Id | Element.Path | Element.Base.Path");
                Debug.Print(new string('=', 100));
                foreach (var elem in sd.Snapshot.Element)
                {
                    Debug.WriteLine("{0}  |  {1}  |  {2}", elem.ElementId, elem.Path, elem.Base?.Path);
                }
                // Debug.Unindent();
            }
        }

        [Conditional("DEBUG")]
        private void dumpOutcome(OperationOutcome outcome) => dumpIssues(outcome?.Issue);

        [Conditional("DEBUG")]
        private void dumpIssues(List<OperationOutcome.IssueComponent> issues)
        {
            if (issues != null && issues.Count > 0)
            {
                Debug.WriteLine("===== {0} issues", issues.Count);
                for (int i = 0; i < issues.Count; i++)
                {
                    dumpIssue(issues[i], i);
                }
                Debug.WriteLine("==================================");
            }
        }

        [Conditional("DEBUG")]
        private void dumpIssue(OperationOutcome.IssueComponent issue, int index)
        {
            var sb = new StringBuilder();
            sb.AppendFormat("* Issue #{0}: Severity = '{1}' Code = '{2}'", index, issue.Severity, issue.Code);
            if (issue.Details != null)
            {
                sb.AppendFormat(" Details: '{0}'", string.Join(" | ", issue.Details.Coding.Select(c => c.Code)));
                if (issue.Details.Text != null) sb.AppendFormat(" Text : '{0}'", issue.Details.Text);
            }
            if (issue.Diagnostics != null) { sb.AppendFormat(" Profile: '{0}'", issue.Diagnostics); }
            if (issue.Location != null) { sb.AppendFormat(" Path: '{0}'", string.Join(" | ", issue.Location)); }

            Debug.WriteLine(sb.ToString());
        }


        [TestMethod]
        public async Tasks.Task GenerateSnapshotEmitBaseData()
        {
            // Verify that the SnapshotGenerator events provide stable references to associated base ElementDefinition instances.
            // If two different profile elements have the same type, then the PrepareElement event should provide the exact same
            // reference to the associated base element. The same target ElementDefinition instance should also be contained in
            // the external type profile.

            var source = _testResolver;
            Assert.IsNotNull(source);

            // var sd = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/daf-condition");
            // var sd = await source.FindStructureDefinitionAsync(@"http://example.com/fhir/StructureDefinition/patient-with-extensions");
            // var sd = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/sdc-questionnaire");
            // var sd = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/cqif-guidanceartifact");
            // var sd = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/shareablevalueset");
            // var sd = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/qicore-goal");
            // var sd = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/cqif-guidanceartifact");
            // var sd = await source.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyLocation");
            // var sd = await source.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyPatient");
            // var sd = await source.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyExtension1");
            // var sd = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/CarePlan");

            // var sd = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/Element");
            // var sd = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/Patient");
            // var sd = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/Extension");
            // var sd = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/Meta");
            // var sd = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/Money");

            // var sd = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/cqif-basic-guidance-action");

            // var sd = await source.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/PatientWithExtension");
            // var sd = await source.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/PatientWithCustomIdentifier");

            var sd = await source.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/CustomIdentifier");

            Assert.IsNotNull(sd);
            // dumpReferences(sd);

            var settings = new SnapshotGeneratorSettings(_settings)
            {
                // settings.GenerateExtensionsOnConstraints = true;
                GenerateAnnotationsOnConstraints = true
            };
            _generator = new SnapshotGenerator(source, settings);

            try
            {
                _generator.PrepareBaseProfile += profileHandler;
                _generator.PrepareElement += elementHandler;
                _generator.Constraint += constraintHandler;

                var (_, expanded) = await generateSnapshotAndCompare(sd);

                dumpOutcome(_generator.Outcome);

                assertBaseDefs(expanded, settings);

                if (sd.Url != ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Element))
                {
                    // Element snapshot should be recursively expanded, as it is the fundamental base profile
                    var sdElem = await source.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.Element);
                    Assert.IsNotNull(sdElem);
                    Assert.IsTrue(sdElem.HasSnapshot);
                    Assert.IsTrue(sdElem.Snapshot.IsCreatedBySnapshotGenerator());
                    assertBaseDefs(sdElem, settings);
                }

                if (sd.Url != ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Id))
                {
                    // Id snapshot should not be (re-)generated, as derived profiles don't force expansion
                    var sdId = await source.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.Id);
                    Assert.IsNotNull(sdId);
                    Assert.IsTrue(sdId.HasSnapshot);
                    Assert.IsFalse(sdId.Snapshot.IsCreatedBySnapshotGenerator());
                    // Re-generate the snapshot and verify base references
                    (_, expanded) = await generateSnapshotAndCompare(sdId);
                    assertBaseDefs(expanded, settings);
                }

                if (sd.Url == @"http://example.org/fhir/StructureDefinition/MyPatient")
                {
                    var sdBase = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/Patient");
                    assertBaseDefs(sdBase, settings);

                    var sdElem = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/Element");
                    assertBaseDefs(sdElem, settings);

                    var sdExt = await source.FindStructureDefinitionAsync(@"http://hl7.org/fhir/StructureDefinition/Extension");
                    assertBaseDefs(sdExt, settings);

                    var sdExt1 = await source.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyExtension1");
                    assertBaseDefs(sdExt1, settings);

                    var sdExt2 = await source.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyExtension2");
                    assertBaseDefs(sdExt2, settings);
                }

            }
            finally
            {
                // Detach event handlers
                _generator.Constraint -= constraintHandler;
                _generator.PrepareElement -= elementHandler;
                _generator.PrepareBaseProfile -= profileHandler;
            }
        }

        [TestMethod]
        public async Tasks.Task TestBaseAnnotations_ExplicitCoreTypeProfile()
        {
            // Verify processing of explicit core element type profile in differential
            // e.g. if the differential specifies explicit core type profile url
            // Example: Patient.identifier type = { Code : Identifier, Profile : "http://hl7.org/fhir/StructureDefinition/Identifier" } }
            // Snapshot generator should ignore this, i.e. NOT treat this as a constraint

            var source = _testResolver;
            Assert.IsNotNull(source);
            var sd = await source.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/PatientWithExplicitCoreIdentifierProfile");

            Assert.IsNotNull(sd);
            // dumpReferences(sd);

            // Patient.identifier should reference the default core Identifier type profile
            var elem = sd.Differential.Element.FirstOrDefault(e => e.Path == "Patient.identifier");
            Assert.IsNotNull(elem);
            // [WMR 20181212] R4 Fixed - inspect the first type profile, compare canonical
            var typeProfileUrl = elem.Type.FirstOrDefault().Profile.FirstOrDefault();
            Assert.IsNotNull(typeProfileUrl);
            Assert.AreEqual(typeProfileUrl, ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Identifier).Value);

            var settings = new SnapshotGeneratorSettings(_settings)
            {
                GenerateAnnotationsOnConstraints = true
            };
            _generator = new SnapshotGenerator(source, settings);

            try
            {
                _generator.PrepareBaseProfile += profileHandler;
                _generator.PrepareElement += elementHandler;
                _generator.Constraint += constraintHandler;

                var (_, expanded) = await generateSnapshotAndCompare(sd);
                dumpOutcome(_generator.Outcome);
                Assert.IsTrue(expanded.HasSnapshot);
                Assert.IsTrue(expanded.Snapshot.IsCreatedBySnapshotGenerator());
                assertBaseDefs(expanded, settings);

                // Verify that the snapshot generator also expanded the referenced core Identifier type profile
                var sdType = await source.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.Identifier);
                Assert.IsNotNull(sdType);
                Assert.IsTrue(sdType.HasSnapshot);
                Assert.IsTrue(sdType.Snapshot.IsCreatedBySnapshotGenerator());

                // Verify the snapshot expansion of the Patient.identifier element
                elem = expanded.Snapshot.Element.FirstOrDefault(e => e.Path == "Patient.identifier");
                Assert.IsNotNull(elem);
                var baseElem = elem.Annotation<BaseDefAnnotation>()?.BaseElementDefinition;
                Assert.IsNotNull(baseElem);
                Assert.AreEqual(elem.Path, baseElem.Path); // Base = core Patient.identifier element
                // Note: diff elem is not exactly equal to base elem (due to reduntant type profile constraint)
                var hasConstraints = !SnapshotGeneratorTest2.isAlmostExactly(elem, baseElem, false);
                Assert.IsTrue(hasConstraints);
                // Check: re-assert while ignoring the redundant type profile constraint
                Assert.IsTrue(SnapshotGeneratorTest2.isAlmostExactly(elem, baseElem, true));

                Assert.IsTrue(hasChanges(elem));

                // Verify base annotations on Patient.identifier subtree
                var elems = expanded.Snapshot.Element.Where(e => e.Path.StartsWith("Patient.identifier.")).ToList();
                for (int i = 0; i < elems.Count; i++)
                {
                    elem = elems[i];
                    Assert.IsNotNull(elem);
                    baseElem = elem.Annotation<BaseDefAnnotation>()?.BaseElementDefinition;
                    Assert.IsNotNull(baseElem);
                    hasConstraints = !SnapshotGeneratorTest2.isAlmostExactly(elem, baseElem);
                    // Only the .use child element has a profile diff constraint
                    bool isConstrained = elem.Path == "Patient.identifier.use";

                    Assert.AreEqual(isConstrained, hasConstraints);

                    var elemHasChanges = hasChanges(elem);
                    Assert.AreEqual(isConstrained, elemHasChanges);

                    // Verify that base element annotations reference the associated child element in Core Identifier profile
                    // [WMR 20170501] OBSOLETE
                    // Assert.AreEqual("Patient." + baseElem.Path.Uncapitalize(), elem.Path);
                    //Debug.WriteLine($"*** elem.Path = '{elem.Path}' baseElem.Path = '{baseElem.Path}' ");
                }

            }
            finally
            {
                // Detach event handlers
                _generator.Constraint -= constraintHandler;
                _generator.PrepareElement -= elementHandler;
                _generator.PrepareBaseProfile -= profileHandler;
            }
        }

        [TestMethod]
        public async Tasks.Task TestBaseAnnotations_CustomTypeProfile()
        {
            // Verify generated base annotations for a profile that references an external element type profile
            // e.g. Patient profile with a custom Identifier profile on the Patient.identifier element

            var source = _testResolver;
            Assert.IsNotNull(source);
            var sd = await source.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/PatientWithCustomIdentifier");

            Assert.IsNotNull(sd);
            // dumpReferences(sd);

            // Patient.identifier should reference an external type profile
            var elem = sd.Differential.Element.FirstOrDefault(e => e.Path == "Patient.identifier");
            Assert.IsNotNull(elem);
            var typeProfileUrl = elem.Type.FirstOrDefault().Profile.FirstOrDefault();
            Assert.IsNotNull(typeProfileUrl);

            var settings = new SnapshotGeneratorSettings(_settings)
            {
                GenerateAnnotationsOnConstraints = true
            };
            _generator = new SnapshotGenerator(source, settings);

            try
            {
                _generator.PrepareBaseProfile += profileHandler;
                _generator.PrepareElement += elementHandler;
                _generator.Constraint += constraintHandler;

                var (_, expanded) = await generateSnapshotAndCompare(sd);
                dumpOutcome(_generator.Outcome);
                Assert.IsTrue(expanded.HasSnapshot);
                Assert.IsTrue(expanded.Snapshot.IsCreatedBySnapshotGenerator());
                assertBaseDefs(expanded, settings);

                // Verify that the snapshot generator also expanded the referenced external custom Identifier type profile
                var sdType = await source.FindStructureDefinitionAsync(typeProfileUrl);
                Assert.IsNotNull(sdType);
                Assert.IsTrue(sdType.HasSnapshot);
                Assert.IsTrue(sdType.Snapshot.IsCreatedBySnapshotGenerator());
                assertBaseDefs(sdType, settings);

                // Verify the snapshot expansion of the Patient.identifier element
                elem = expanded.Snapshot.Element.FirstOrDefault(e => e.Path == "Patient.identifier");
                Assert.IsNotNull(elem);
                var baseElem = elem.Annotation<BaseDefAnnotation>()?.BaseElementDefinition;
                Assert.IsNotNull(baseElem);
                Assert.AreEqual(elem.Path, baseElem.Path); // Base = core Patient.identifier element
                // Note: diff elem is not exactly equal to base elem (due to reduntant type profile constraint)
                // hasConstraints and hasChanges methods aren't smart enough to detect redundant constraints
                var hasConstraints = !SnapshotGeneratorTest2.isAlmostExactly(elem, baseElem);
                Assert.IsTrue(hasConstraints);

                // Verify base annotations on Patient.identifier subtree
                var elems = expanded.Snapshot.Element.Where(e => e.Path.StartsWith("Patient.identifier.")).ToList();
                for (int i = 0; i < elems.Count; i++)
                {
                    elem = elems[i];
                    Assert.IsNotNull(elem);
                    baseElem = elem.Annotation<BaseDefAnnotation>()?.BaseElementDefinition;
                    Assert.IsNotNull(baseElem);
                    hasConstraints = !SnapshotGeneratorTest2.isAlmostExactly(elem, baseElem);
                    // Only the .use child element has a profile diff constraint
                    bool isConstrained = elem.Path == "Patient.identifier.use" || elem.Path == "Patient.identifier.value";
                    Assert.AreEqual(isConstrained, hasConstraints);
                    Assert.AreEqual(isConstrained, hasChanges(elem));

                    // Verify that base element annotations reference the associated child element in custom Identifier profile
                    // Assert.AreEqual("Patient." + baseElem.Path.Uncapitalize(), elem.Path);

                    // Verify correct base element annotations
                    // Should point to rebased custom type element (same path)
                    Assert.AreEqual(baseElem.Path, elem.Path);
                }

                // Verify specific element constraints
                // Patient.identifier.use::min is overriden by patient profile
                elem = elems.FirstOrDefault(e => e.Path == "Patient.identifier.use");
                Assert.IsNotNull(elem);
                Assert.AreEqual(1, elem.Min);
                Assert.IsTrue(elem.HasDiffConstraintAnnotations());
                Assert.IsTrue(elem.MinElement.IsConstrainedByDiff());

                // Patient.identifier.value::short is overriden by patient profile
                elem = elems.FirstOrDefault(e => e.Path == "Patient.identifier.value");
                Assert.IsNotNull(elem);
                Assert.AreEqual("A custom identifier value", elem.Short);
                Assert.IsTrue(elem.HasDiffConstraintAnnotations());
                Assert.IsTrue(elem.ShortElement.IsConstrainedByDiff());

                // Patient.identifier.system::min is inherited from custom type profile, not overriden by patient profile
                elem = elems.FirstOrDefault(e => e.Path == "Patient.identifier.system");
                Assert.IsNotNull(elem);
                Assert.AreEqual(1, elem.Min);
                Assert.IsFalse(elem.HasDiffConstraintAnnotations());
                Assert.IsFalse(elem.MinElement.IsConstrainedByDiff());

            }
            finally
            {
                // Detach event handlers
                _generator.Constraint -= constraintHandler;
                _generator.PrepareElement -= elementHandler;
                _generator.PrepareBaseProfile -= profileHandler;
            }
        }

        [TestMethod]
        public async Tasks.Task TestBaseAnnotations_InlineExtension()
        {
            // Verify generated base annotations for a profile that references an external extension definition profile

            var source = _testResolver;
            Assert.IsNotNull(source);
            var sd = await source.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/PatientWithExtension");

            Assert.IsNotNull(sd);
            // dumpReferences(sd);

            // Patient profile should reference an external extension definition, fetch the url
            var elem = sd.Differential.Element.FirstOrDefault(e => e.Path == "Patient.extension" && e.Slicing == null);
            Assert.IsNotNull(elem);
            var extensionDefinitionUrl = elem.Type.FirstOrDefault().Profile.FirstOrDefault();
            Assert.IsNotNull(extensionDefinitionUrl);

            var settings = new SnapshotGeneratorSettings(_settings)
            {
                GenerateAnnotationsOnConstraints = true
            };
            _generator = new SnapshotGenerator(source, settings);

            try
            {
                _generator.PrepareBaseProfile += profileHandler;
                _generator.PrepareElement += elementHandler;
                _generator.Constraint += constraintHandler;

                var (_, expanded) = await generateSnapshotAndCompare(sd);
                dumpOutcome(_generator.Outcome);
                Assert.IsTrue(expanded.HasSnapshot);
                Assert.IsTrue(expanded.Snapshot.IsCreatedBySnapshotGenerator());
                assertBaseDefs(expanded, settings);

                // Verify that the snapshot generator also expanded the referenced external extension definition
                var sdExtension = await source.FindStructureDefinitionAsync(extensionDefinitionUrl);
                Assert.IsNotNull(sdExtension);
                Assert.IsTrue(sdExtension.HasSnapshot);
                Assert.IsTrue(sdExtension.Snapshot.IsCreatedBySnapshotGenerator());
                assertBaseDefs(sdExtension, settings);

                // Verify correct merging of inline profile constraints overriding the extension definition
                var nav = new ElementDefinitionNavigator(expanded);
                Assert.IsTrue(nav.MoveToFirstChild());
                Assert.IsTrue(nav.MoveToFirstChild());
                Assert.IsTrue(nav.MoveToNext("extension"));
                Assert.IsNotNull(nav.Current.Slicing);  // Extension slicing entry
                Assert.IsTrue(nav.MoveToNext("extension"));
                elem = nav.Current;
                Assert.IsNull(elem.Slicing);    // First extension
                Assert.AreEqual(elem.PrimaryTypeProfile(), extensionDefinitionUrl);

                Assert.AreEqual("extension", elem.SliceName);
                Assert.AreEqual("1", elem.Max); // Inline profile constraint overriding the extension definition
                Assert.IsTrue(elem.MaxElement.IsConstrainedByDiff());
                Assert.IsTrue(elem.HasDiffConstraintAnnotations());
                Assert.IsTrue(elem.IsConstrainedByDiff());
                var baseElem = elem.Annotation<BaseDefAnnotation>()?.BaseElementDefinition;
                Assert.IsNotNull(baseElem);
                Assert.AreEqual("*", baseElem.Max);             // Verify that max property is not inherited from base element = Extension root element
                Assert.AreEqual(baseElem.Short, elem.Short);    // Verify that short property is inherited
                Assert.IsFalse(elem.ShortElement.IsConstrainedByDiff());
                // Profile overrides the definition property of the extension definition root element 
                Assert.AreNotEqual(baseElem.Definition, elem.Definition);
                Assert.IsTrue(elem.DefinitionElement.IsConstrainedByDiff());

                Assert.IsTrue(nav.MoveToFirstChild());

                Assert.IsTrue(nav.MoveToNext("url"));
                elem = nav.Current;
                Assert.IsFalse(elem.HasDiffConstraintAnnotations());
                var uri = elem.Fixed as FhirUri;
                Assert.IsNotNull(uri);
                Assert.AreEqual(extensionDefinitionUrl, uri.Value);

#if NORMALIZE_RENAMED_TYPESLICE
                // [WMR 20190828] R4: Normalize renamed type slices in snapshot
                Assert.IsTrue(nav.MoveToNext("value[x]"));          // Slice entry
                Assert.IsTrue(nav.MoveToNextSlice("valueString"));  // Type slice
#else
                Assert.IsTrue(nav.MoveToNext("valueString"));
#endif
                elem = nav.Current;
                Assert.AreEqual(1, elem.Min);            // Inline profile constraint overriding the extension definition
                Assert.IsTrue(elem.MinElement.IsConstrainedByDiff());
                Assert.IsTrue(elem.HasDiffConstraintAnnotations());
                baseElem = elem.Annotation<BaseDefAnnotation>()?.BaseElementDefinition;
                Assert.IsNotNull(baseElem);
                Assert.AreEqual(0, baseElem.Min);               // Verify that min property is not inherited from base element = Extension.valueString
                Assert.AreEqual(baseElem.Short, elem.Short);    // Verify that short property is inherited
                Assert.IsFalse(elem.ShortElement.IsConstrainedByDiff());
                // Verify that definition property is inherited
                // [WMR 20181212] R4 - Definition type changed from string to markdown
                Assert.IsTrue(elem.DefinitionElement.IsExactly(baseElem.DefinitionElement));
                Assert.IsFalse(elem.DefinitionElement.IsConstrainedByDiff());
            }
            finally
            {
                // Detach event handlers
                _generator.Constraint -= constraintHandler;
                _generator.PrepareElement -= elementHandler;
                _generator.PrepareBaseProfile -= profileHandler;
            }
        }

        [TestMethod]
        public async Tasks.Task TestBaseAnnotations_StructureDefinition_ExtensionHeaderSlicingElement()
        {
            const string url = @"https://example.org/fhir/StructureDefinition/MyPatient";
            const string urlExtension = @"http://hl7.org/fhir/StructureDefinition/cqf-initialValue";
            const string sliceName = "initialValue";
            const string path = "Patient.active.extension";

            var sd = new StructureDefinition()
            {
                Type = FHIRAllTypes.Patient.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
                Name = "MyPatient",
                Url = url,
                Abstract = false,
                FhirVersion = FHIRVersion.N4_0_1,
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition(path)
                        {
                        },
                        new ElementDefinition(path)
                        {
                            SliceName = sliceName,
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = "Extension",
                                    ProfileElement = new List<Canonical>()
                                    {
                                        new Canonical(urlExtension)
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var source = new CachedResolver(new MultiResolver(_standardFhirSource, new InMemoryResourceResolver(sd)));

            var settings = new SnapshotGeneratorSettings(_settings)
            {
                GenerateAnnotationsOnConstraints = true
            };
            _generator = new SnapshotGenerator(source, settings);

            try
            {
                _generator.PrepareBaseProfile += profileHandler;
                _generator.PrepareElement += elementHandler;
                _generator.Constraint += constraintHandler;

                var expanded = await _generator.GenerateAsync(sd);
                dumpOutcome(_generator.Outcome);
                assertBaseDefs(expanded, settings);

                var header = expanded.Single(e => e.Path == path && e.SliceName == null);
                var extension = expanded.Single(e => e.Path == path && e.SliceName == sliceName);

                Assert.IsTrue(header.TryGetAnnotation<BaseDefAnnotation>(out var annoHeader));
                Assert.IsTrue(extension.TryGetAnnotation<BaseDefAnnotation>(out var annoExtension));

                Assert.IsTrue(header.Slicing != null);
                Assert.IsTrue(extension.Slicing == null);

                Assert.IsTrue(annoHeader.BaseElementDefinition.Slicing != null);
                Assert.IsTrue(annoExtension.BaseElementDefinition.Slicing == null);
            }
            finally
            {
                // Detach event handlers
                _generator.Constraint -= constraintHandler;
                _generator.PrepareElement -= elementHandler;
                _generator.PrepareBaseProfile -= profileHandler;
            }
        }

        // [WMR 20190805] Updated, verify base annotation on extension definition root element
        // Should point to core "Extension", not "Element"
        [TestMethod]
        public async Tasks.Task TestBaseAnnotations_ExtensionDefinition()
        {
            const string url = @"http://example.org/fhir/StructureDefinition/MyTestExtension";
            var sd = new StructureDefinition()
            {
                Type = FHIRAllTypes.Extension.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Extension),
                Name = "MyTestExtension",
                Url = url,
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.ComplexType,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Extension")
                        {
                            Short = "TEST"
                        },
                        new ElementDefinition("Extension.url")
                        {
                            Fixed = new FhirString(url)
                        }
                    }
                }
            };

            var source = new CachedResolver(new MultiResolver(_standardFhirSource, new InMemoryResourceResolver(sd)));

            var settings = new SnapshotGeneratorSettings(_settings)
            {
                GenerateAnnotationsOnConstraints = true
            };
            _generator = new SnapshotGenerator(source, settings);

            try
            {
                _generator.PrepareBaseProfile += profileHandler;
                _generator.PrepareElement += elementHandler;
                _generator.Constraint += constraintHandler;


                // Replace root element and re-expand
                var coreExtension = await source.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.Extension);

                // [WMR 20190806] SnapGen should never expose/leak internal annotations
                //Debug.Assert(!coreExtension.Differential.Element[0].HasSnapshotElementAnnotation());
                Assert.IsFalse(coreExtension.Differential.Element[0].HasSnapshotElementAnnotation());

                Assert.IsNotNull(coreExtension);
                coreExtension.Snapshot = null;
                await _generator.UpdateAsync(coreExtension);

                Assert.IsTrue(coreExtension.HasSnapshot);
                var coreDiffRoot = coreExtension.Differential.Element[0];
                var coreSnapRoot = coreExtension.Snapshot.Element[0];

                // [WMR 20190806] SnapGen should never expose/leak internal annotations
                //Debug.Assert(!coreDiffRoot.HasSnapshotElementAnnotation());
                Assert.IsFalse(coreDiffRoot.HasSnapshotElementAnnotation());

                var userDiffRoot = (ElementDefinition)coreDiffRoot.DeepCopy();

                sd.Differential.Element[0] = userDiffRoot;

                var expanded = await _generator.GenerateAsync(sd);
                dumpOutcome(_generator.Outcome);
                assertBaseDefs(expanded, settings);

                var userSnapRoot = expanded[0];
                Assert.AreNotSame(coreSnapRoot, userSnapRoot);
                Assert.IsTrue(userSnapRoot.TryGetAnnotation<BaseDefAnnotation>(out var anno));
                Assert.AreEqual("Extension", anno.BaseStructureDefinition.Name);
                Assert.AreEqual("Extension", anno.BaseElementDefinition.Path);

                // [WMR 20190806] SnapGen should never expose/leak internal annotations
                //Debug.Assert(!userDiffRoot.HasSnapshotElementAnnotation());
                Assert.IsFalse(userDiffRoot.HasSnapshotElementAnnotation());

            }
            finally
            {
                // Detach event handlers
                _generator.Constraint -= constraintHandler;
                _generator.PrepareElement -= elementHandler;
                _generator.PrepareBaseProfile -= profileHandler;
            }
        }

        // [WMR 20170714] NEW
        // Annotated Base Element for backbone elements is not included in base structuredefinition ?

        private static StructureDefinition MyTestObservation => new()
        {
            Type = FHIRAllTypes.Observation.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation),
            Name = "MyTestObservation",
            Url = "http://example.org/fhir/StructureDefinition/MyTestObservation",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.Resource,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Observation.component")
                    {
                        Min = 1
                    },
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestBaseAnnotations_BackboneElement()
        {
            var sd = MyTestObservation;
            var resolver = new InMemoryResourceResolver(sd);
            var multiResolver = new MultiResolver(_testResolver, resolver);

            _generator = new SnapshotGenerator(multiResolver, _settings);

            StructureDefinition expanded = null;
            _generator.PrepareElement += elementHandler;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(sd);
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }

            dumpOutcome(_generator.Outcome);
            Assert.IsTrue(expanded.HasSnapshot);

            Debug.WriteLine("Core Observation:");
            var obs = await _testResolver.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.Observation);
            Assert.IsNotNull(obs);
            Assert.IsTrue(obs.HasSnapshot);
            dumpBaseElems(obs.Snapshot.Element);

            Debug.WriteLine("Derived Observation:");
            // dumpElements(expanded.Snapshot.Element);
            dumpBaseElems(expanded.Snapshot.Element);

            assertBaseDefs(expanded, _settings);

            // Additional check: verify that all annotated base element references
            // point to existing instances in the base profile snapshot
            var elems = expanded.Snapshot.Element;
            var baseElems = obs.Snapshot.Element;
            for (int i = 0; i < elems.Count; i++)
            {
                var elem = elems[i];
                var baseElem = elem.Annotation<BaseDefAnnotation>()?.BaseElementDefinition;
                Assert.IsTrue(baseElems.Contains(baseElem));
            }
        }


        // [WMR 20160816] Test custom annotations containing associated base definitions
        private class BaseDefAnnotation
        {
            public BaseDefAnnotation(ElementDefinition baseElemDef, StructureDefinition baseStructDef)
            {
                BaseElementDefinition = baseElemDef;
                BaseStructureDefinition = baseStructDef;
            }
            public ElementDefinition BaseElementDefinition { get; private set; }
            public StructureDefinition BaseStructureDefinition { get; private set; }
        }

        private static ElementDefinition GetBaseElementAnnotation(ElementDefinition elemDef)
        {
            return elemDef?.Annotation<BaseDefAnnotation>()?.BaseElementDefinition;
        }

        private void profileHandler(object sender, SnapshotBaseProfileEventArgs e)
        {
            var profile = e.Profile;
            // Assert.IsTrue(sd.Url != profile.Url || sd.IsExactly(profile));
            var baseProfile = e.BaseProfile;
            Assert.IsNotNull(baseProfile);
            Debug.WriteLine("[SnapshotBaseProfileHandler] Profile #{0} '{1}' BaseDefinition = '{2}'".FormatWith(profile.GetHashCode(), profile.Url, profile.BaseDefinition));
            Debug.WriteLine("[SnapshotBaseProfileHandler] Base Profile #{0} '{1}'".FormatWith(baseProfile.GetHashCode(), baseProfile.Url));
            var rootElem = baseProfile.Snapshot.Element[0];
            Debug.WriteLine("[SnapshotBaseProfileHandler] Base Root element #{0} '{1}'".FormatWith(rootElem.GetHashCode(), rootElem.Path));
            Assert.AreEqual(profile.BaseDefinition, baseProfile.Url);
        }

        private static void elementHandler(object sender, SnapshotElementEventArgs e)
        {
            var elem = e.Element;
            Assert.IsNotNull(elem);

            // Assert.IsNotNull(elem.Base);

            var ann = elem.Annotation<BaseDefAnnotation>();
            // We want to annotate a reference to the matching base element from the (immediate) base profile.
            // When the snapshot generator expands external profiles, then this handler is called once for each
            // profile in the base hierarchy, starting at the root profile, e.g. Resource => DomainResource => Patient.
            // Each time we recreate the annotation, so the final annotation contains a reference to the immediate base.
            if (!(ann is null))
            {
                elem.RemoveAnnotations<BaseDefAnnotation>();
            }
            var baseDef = e.BaseElement;
            var baseStruct = e.BaseStructure;
            elem.AddAnnotation(new BaseDefAnnotation(baseDef, baseStruct));

            Debug.Write($"[{nameof(SnapshotGeneratorTest)}.{nameof(elementHandler)}] #{elem.GetHashCode()} '{elem.Path}:{elem.SliceName}' - Base: #{baseDef?.GetHashCode() ?? 0} '{(baseDef?.Path)}' - Base Structure '{baseStruct?.Url}'");
            Debug.WriteLine(ann?.BaseElementDefinition != null ? $" (old Base: #{ann.BaseElementDefinition.GetHashCode()} '{ann.BaseElementDefinition.Path}')" : "");
        }

        private void constraintHandler(object sender, SnapshotConstraintEventArgs e)
        {
            if (e.Element is ElementDefinition elem)
            {
                var changed = elem.IsConstrainedByDiff();
                //Debug.Assert(!_settings.GenerateAnnotationsOnConstraints || changed);
                Assert.IsTrue(!_settings.GenerateAnnotationsOnConstraints || changed);
                Debug.WriteLine("[SnapshotConstraintHandler] #{0} '{1}'{2}".FormatWith(elem.GetHashCode(), elem.Path, changed ? " CHANGED!" : null));
            }
        }

        private static void assertBaseDefs(StructureDefinition sd, SnapshotGeneratorSettings settings)
        {
            Assert.IsNotNull(sd);
            Assert.IsNotNull(sd.Snapshot);
            Debug.WriteLine("\r\nStructureDefinition '{0}' url = '{1}'", sd.Name, sd.Url);
            assertBaseDefs(sd.Snapshot.Element, settings);
        }

        private static void assertBaseDefs(List<ElementDefinition> elems, SnapshotGeneratorSettings settings)
        {
            Assert.IsNotNull(elems);
            Assert.IsTrue(elems.Count > 0);

            //var isConstraint = sd.Derivation == StructureDefinition.TypeDerivationRule.Constraint;

            Debug.WriteLine("# | Constraints? | Changed? | Element.Path | Element.Base.Path | BaseElement.Path | #Base | Redundant?");
            Debug.WriteLine(new string('=', 100));

            foreach (var elem in elems)
            {
                // Each element should have a valid Base component, unless the profile is a core type/resource definition (no base)
                // Assert.IsTrue(!isConstraint || elem.Base != null);

                var ann = elem.Annotation<BaseDefAnnotation>();
                var baseDef = ann?.BaseElementDefinition;
                Assert.AreNotEqual(elem, baseDef);

                var hasChanges = SnapshotGeneratorTest2.hasChanges(elem);
                var isNotExactly = false;
                if (baseDef != null) // && elem.Base != null)
                {
                    // If normalizing, then elem.Base.Path refers to the defining profile (e.g. DomainResource),
                    // whereas baseDef refers to the immediate base profile (e.g. Patient)
                    //Debug.Assert(
                    Assert.IsTrue(elem.Base == null || ElementDefinitionNavigator.IsCandidateBasePath(elem.Base.Path, baseDef.Path)
                        // [WMR 20170713] Added, e.g. Patient.identifier.use <=> code
                        || !baseDef.Path.Contains(".")
                        );
                    isNotExactly = !SnapshotGeneratorTest2.isAlmostExactly(elem, baseDef);
                }
                // var isValid = hasChanges == isNotExactly;
                var isRedundant = hasChanges && !isNotExactly;
                bool? hasConstraintAnnotations = null;
                if (settings.GenerateAnnotationsOnConstraints)
                {
                    hasConstraintAnnotations = elem.HasDiffConstraintAnnotations();
                    //isValid &= isNotExactly == hasConstraintAnnotations;
                    isRedundant |= !isNotExactly && (hasConstraintAnnotations == true);
                }

                Debug.WriteLine("{0,10}  |  {1}  |  {2,-12}  |  {3,-50}  |  {4,-40}  |  {5,-40}  |  {6,10}  |  {7}",
                    elem.GetHashCode(),
                    (isNotExactly ? "+" : "-")
                    + (hasConstraintAnnotations.HasValue ? (hasConstraintAnnotations.Value ? " (+)" : " (-)") : null),
                    getChangeDescription(elem),
                    elem.Path,
                    elem.Base?.Path,
                    baseDef?.Path,
                    baseDef?.GetHashCode().ToString(),
                    // !isValid ? "!!!" : ""
                    isRedundant ? "(redundant)" : ""
                );
                //Assert.IsTrue(baseDef == null || isValid);
                // Debug.Assert(baseDef == null || isValid);
            }
        }

        // Utility function to compare element and base element
        // Path, Base and CHANGED_BY_DIFF_EXT extension are excluded from comparison
        // Returns true if the element has no other constraints on base
        private static bool isAlmostExactly(ElementDefinition elem, ElementDefinition baseElem, bool ignoreTypeProfile = false)
        {
            var elemClone = (ElementDefinition)elem.DeepCopy();
            var baseClone = (ElementDefinition)baseElem.DeepCopy();

            // Id, Path & Base are expected to differ
            baseClone.ElementId = elem.ElementId;
            baseClone.Path = elem.Path;
            baseClone.Base = elem.Base;

            // [WMR 20170713] Added
            if (ignoreTypeProfile)
            {
                //Debug.Assert(elem.Type.Count > 0);
                //Debug.Assert(baseClone.Type.Count > 0);
                Assert.IsTrue(elem.Type.Count > 0);
                Assert.IsTrue(baseClone.Type.Count > 0);
                baseClone.Type[0].Profile = elem.Type[0].Profile;
            }

            // Also ignore any Changed extensions on base and diff
            elemClone.RemoveAllConstrainedByDiffExtensions();
            baseClone.RemoveAllConstrainedByDiffExtensions();
            elemClone.RemoveAllConstrainedByDiffAnnotations();
            baseClone.RemoveAllConstrainedByDiffAnnotations();

            var result = baseClone.IsExactly(elemClone);
            return result;
        }

        // Returns true if the specified element or any of its' components contain the CHANGED_BY_DIFF_EXT extension
        private static bool hasChanges(ElementDefinition elem)
        {
            return isChanged(elem)
                || hasChanges(elem.AliasElement)
                || isChanged(elem.Base)
                || isChanged(elem.Binding)
                || hasChanges(elem.Code)
                || isChanged(elem.CommentElement)
                || hasChanges(elem.ConditionElement)
                || hasChanges(elem.Constraint)
                || isChanged(elem.DefaultValue)
                || isChanged(elem.DefinitionElement)
                || hasChanges(elem.Example)
                || hasChanges(elem.Extension)
                //   || hasChanges(elem.FhirCommentsElement)
                || isChanged(elem.Fixed)
                || isChanged(elem.IsModifierElement)
                || isChanged(elem.IsSummaryElement)
                || isChanged(elem.LabelElement)
                || hasChanges(elem.Mapping)
                || isChanged(elem.MaxElement)
                || isChanged(elem.MaxLengthElement)
                || isChanged(elem.MaxValue)
                || isChanged(elem.MeaningWhenMissingElement)
                || isChanged(elem.MinElement)
                || isChanged(elem.MinValue)
                || isChanged(elem.MustSupportElement)
                || isChanged(elem.SliceNameElement)
                || isChanged(elem.ContentReferenceElement)
                || isChanged(elem.PathElement)
                || isChanged(elem.Pattern)
                || hasChanges(elem.RepresentationElement)
                || isChanged(elem.RequirementsElement)
                || isChanged(elem.ShortElement)
                || isChanged(elem.Slicing)
                || hasChanges(elem.Type);
        }

        private static string getChangeDescription(ElementDefinition element)
        {
            if (isChanged(element.Slicing)) { return "Slicing"; }       // Moved to front
            if (hasChanges(element.Type)) { return "Type"; }            // Moved to front
            if (isChanged(element.ShortElement)) { return "Short"; }    // Moved to front

            if (hasChanges(element.AliasElement)) { return "Alias"; }
            if (isChanged(element.Base)) { return "Base"; }
            if (isChanged(element.Binding)) { return "Binding"; }
            if (hasChanges(element.Code)) { return "Code"; }
            if (isChanged(element.CommentElement)) { return "Comment"; }
            if (hasChanges(element.ConditionElement)) { return "Condition"; }
            if (hasChanges(element.Constraint)) { return "Constraint"; }
            if (isChanged(element.DefaultValue)) { return "DefaultValue"; }
            if (isChanged(element.DefinitionElement)) { return "Definition"; }
            if (hasChanges(element.Example)) { return "Example"; }
            if (hasChanges(element.Extension)) { return "Extension"; }
            //if (hasChanges(element.FhirCommentsElement)) { return "FhirComments"; }
            if (isChanged(element.Fixed)) { return "Fixed"; }
            if (isChanged(element.IsModifierElement)) { return "IsModifier"; }
            if (isChanged(element.IsSummaryElement)) { return "IsSummary"; }
            if (isChanged(element.LabelElement)) { return "Label"; }
            if (hasChanges(element.Mapping)) { return "Mapping"; }
            if (isChanged(element.MaxElement)) { return "Max"; }
            if (isChanged(element.MaxLengthElement)) { return "MaxLength"; }
            if (isChanged(element.MaxValue)) { return "MaxValue"; }
            if (isChanged(element.MeaningWhenMissingElement)) { return "MeaningWhenMissing"; }
            if (isChanged(element.MinElement)) { return "Min"; }
            if (isChanged(element.MinValue)) { return "MinValue"; }
            if (isChanged(element.MustSupportElement)) { return "MustSupport"; }
            if (isChanged(element.SliceNameElement)) { return "SliceName"; }
            if (isChanged(element.ContentReferenceElement)) { return "ContentReference"; }
            if (isChanged(element.PathElement)) { return "Path"; }
            if (isChanged(element.Pattern)) { return "Pattern"; }
            if (hasChanges(element.RepresentationElement)) { return "Representation"; }
            if (isChanged(element.RequirementsElement)) { return "Requirements"; }
            //if (IsChanged(element.ShortElement)) { return "Short"; }
            //if (IsChanged(element.Slicing)) { return "Slicing"; }
            //if (HasChanges(element.Type)) { return "Type"; }

            return isChanged(element) ? "Element" : string.Empty;           // Moved to back
        }

        private static bool hasChanges<T>(IList<T> elements) where T : Element => elements != null ? elements.Any(e => isChanged(e)) : false;
        private static bool isChanged(Element elem) => elem != null && elem.IsConstrainedByDiff();

        [TestMethod]
        public async Tasks.Task TestExpandCoreElement()
        {
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/Element");
        }

        [TestMethod]
        public async Tasks.Task TestExpandCoreBackBoneElement()
        {
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/BackboneElement");
        }

        [TestMethod]
        public async Tasks.Task TestExpandCoreExtension()
        {
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/Extension");
        }

        // [WMR 20190130] DEBUGGING
        [TestMethod]
        public async Tasks.Task TestExpandQuestionnaireResource()
        {
            // TODO: Fix empty base for Questionnaire.item.item
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/Questionnaire");
        }


        // [WMR 20190130] DEBUGGING
        [TestMethod]
        public async Tasks.Task TestExpandCoreArtifacts()
        {
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/integer");
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/positiveInt");
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/string");
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/code");
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/id");

            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/Meta");
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/HumanName");
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/Quantity");
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/SimpleQuantity");
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/Money");

            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/Resource");
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/DomainResource");

            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/Basic");
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/Patient");
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/Questionnaire");
            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/AuditEvent");

            await testExpandResource(@"http://hl7.org/fhir/StructureDefinition/Organization");
        }

        [TestMethod]
        public async Tasks.Task TestExpandAllCoreTypes()
        {
            // these are the types that are part of R5, but retrospectively introduced
            // as POCOs already in R3. There are no StructDefs available for these,
            // so we should not try to expand them.
            var r5types = new string[] { "BackboneType", "Base", "DataType", "PrimitiveType" };

            // Generate snapshots for all core types, in the original order as they are defined
            // The Snapshot Generator should recursively process any referenced base/type profiles (e.g. Element, Extension)
            var coreArtifactNames = ModelInfo.FhirCsTypeToString.Values;
            var coreTypeUrls = coreArtifactNames
                .Where(t => !ModelInfo.IsKnownResource(t))
                .Where(t => !r5types.Contains(t))
                .Select(t => "http://hl7.org/fhir/StructureDefinition/" + t).ToArray();
            await testExpandResources(coreTypeUrls.ToArray());
        }

        [TestMethod]
        [TestCategory("LongRunner")]
        public async Tasks.Task TestExpandAllCoreResources()
        {
            // Generate snapshots for all core resources, in the original order as they are defined
            // The Snapshot Generator should recursively process any referenced base/type profiles (e.g. data types)
            var coreResourceUrls = ModelInfo.SupportedResources.Select(t => "http://hl7.org/fhir/StructureDefinition/" + t);

            // R5, version 4.6.0: StructureDefinition is not correct for Citation (Citation.summary should be BackboneElement)
            coreResourceUrls = coreResourceUrls.Where(url => !url.EndsWith("Citation"));
            await testExpandResources(coreResourceUrls.ToArray());
        }

        private async Tasks.Task testExpandResources(string[] profileUris)
        {
            var sw = new Stopwatch();
            int count = profileUris.Length;
            _source.Reset();
            sw.Start();

            for (int i = 0; i < count; i++)
            {
                await testExpandResource(profileUris[i]);
            }

            sw.Stop();
            _source.ShowDuration(count, sw.Elapsed);
        }

        private async Tasks.Task<bool> testExpandResource(string url)
        {
            Debug.Print("[testExpandResource] url = '{0}'", url);
            var sd = await _testResolver.FindStructureDefinitionAsync(url);
            Assert.IsNotNull(sd);
            // dumpReferences(sd);

            var (result, expanded) = await generateSnapshotAndCompare(sd);

            dumpOutcome(_generator.Outcome);
            dumpBasePaths(expanded);

            if (!result)
            {
                Debug.Print("Expanded is not exactly equal to original... verifying...");
                result = await verifyElementBase(sd, expanded);
            }

            // Core artifact snapshots are incorrect, e.g. url snapshot is missing extension element
            Assert.IsTrue(result);

            return result;
        }

        private IEnumerable<T> enumerateBundleStream<T>(Stream stream) where T : Resource
        {
            using (var reader = XmlReader.Create(stream))
            {
                var parser = new FhirXmlParser();
                var bundle = parser.Parse<Bundle>(reader);
                foreach (var entry in bundle.Entry)
                {
                    if (entry.Resource is T res) { yield return res; }
                }
            }
        }

        [TestMethod]
        public async Tasks.Task TestExpandCoreTypesByHierarchy()
        {
            // [WMR 20160912] Expand all core data types
            // Start at root types without a base (Element, Extension), then recursively expand derived types

            var result = true;
            var resolver = new CachedResolver(ZipSource.CreateValidationSource());

            _generator = new SnapshotGenerator(resolver, _settings);
            _generator.PrepareElement += elementHandler;

            try
            {
                var coreTypes = ModelInfo.FhirCsTypeToString.Values.Where(s => ModelInfo.IsPrimitive(s) || ModelInfo.IsDataType(s));
                var coreDefs = await Tasks.Task.WhenAll(coreTypes.Select(ct => resolver.FindStructureDefinitionForCoreTypeAsync(ct)));
                ProfileInfo[] coreProfileInfo = coreDefs
                    .Where(sd => sd is not null)
                    .Select(sd => new ProfileInfo() { Url = sd.Url, BaseDefinition = sd.BaseDefinition }).ToArray();

                await expandStructuresBasedOn(resolver, coreProfileInfo, null);
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }
            Assert.IsTrue(result);
        }

        private struct ProfileInfo { public string Url; public string BaseDefinition; }

        private async Tasks.Task expandStructuresBasedOn(IAsyncResourceResolver resolver, ProfileInfo[] profileInfo, string baseUrl)
        {
            var derivedStructures = profileInfo.Where(pi => pi.BaseDefinition == baseUrl);
            if (derivedStructures.Any())
            {
                Debug.WriteLineIf(derivedStructures.Any(), "Expand structures derived from: '{0}'".FormatWith(baseUrl));
                foreach (var info in derivedStructures)
                {
                    var sd = await resolver.FindStructureDefinitionAsync(info.Url);
                    Assert.IsNotNull(sd);
                    await updateSnapshot(sd);
                    await expandStructuresBasedOn(resolver, profileInfo, sd.Url);
                }
            }
        }

        private async Tasks.Task updateSnapshot(StructureDefinition sd)
        {
            Assert.IsNotNull(sd);
            Debug.Print("Profile: '{0}' : '{1}'".FormatWith(sd.Url, sd.BaseDefinition));
            // Important! Must expand original instances, not clones!
            // var original = sd.DeepCopy() as StructureDefinition;
            await _generator.UpdateAsync(sd);
            // result &= verifyElementBase(original, entry);
            dumpOutcome(_generator.Outcome);
            dumpBaseElems(sd.Snapshot.Element);
        }

        // Verify ElementDefinition.Base components
        private async Tasks.Task<bool> verifyElementBase(StructureDefinition original, StructureDefinition expanded)
        {
            var originalElems = original.HasSnapshot ? original.Snapshot.Element : new List<ElementDefinition>();
            var expandedElems = expanded.HasSnapshot ? expanded.Snapshot.Element : new List<ElementDefinition>();
            var isConstraint = expanded.Derivation == StructureDefinition.TypeDerivationRule.Constraint;
            Debug.Print("Original has {0} elements, expanded has {1} elements...".FormatWith(originalElems.Count, expandedElems.Count));

            // dumpBasePaths(original);

            bool verified = false;
            if (expandedElems.Count < originalElems.Count)
            {
                for (int i = 0; i < originalElems.Count; i++)
                {
                    var elem = originalElems[i];
                    var match = expandedElems.Any(e => e.Path == elem.Path);
                    if (!match)
                    {
                        Debug.Print("{0} has not been expanded...".FormatWith(elem.Path));
                    }
                }
            }
            else if (expandedElems.Count == originalElems.Count)
            {
                verified = true;

                var rootElemName = expandedElems[0].Path;

                //var baseProfileUrl = expanded.Base;
                //var baseProfile = baseProfileUrl != null ? await _testResolver.FindStructureDefinitionAsync(baseProfileUrl) : null;
                //var baseRootElemName = baseProfile != null && baseProfile.Snapshot != null ? baseProfile.Snapshot.Element[0].Path : null;
                //if (expandedElems.Count > 0 && baseRootElemName != null)
                //{
                //    verified &= verifyBasePath(expandedElems[0], originalElems[0], baseRootElemName);
                //}

                if (expanded.Kind == StructureDefinition.StructureDefinitionKind.PrimitiveType)
                {
                    if (rootElemName != "Element")
                    {
                        // [WMR 20190130] STU3
                        //verified &= verifyBasePath(expandedElems[0], originalElems[0], "Element");
                        // [WMR 20190130] R4
                        verified &= verifyBasePath(expandedElems[0], originalElems[0], rootElemName);
                    }

                    if (rootElemName != "Element" && expandedElems.Count > 2)
                    {
                        verified &= verifyBasePath(expandedElems[1], originalElems[1], "Element.id");
                        verified &= verifyBasePath(expandedElems[2], originalElems[2], "Element.extension");
                    }
                }
                else if (expanded.Kind == StructureDefinition.StructureDefinitionKind.ComplexType)
                {
                    // TODO: verify that this is correct (I think so given the others in this context)
                    verified &= verifyBasePath(expandedElems[1], originalElems[1], "Element.id");
                    verified &= verifyBasePath(expandedElems[2], originalElems[2], "Element.extension");
                }
                else if (expanded.Kind == StructureDefinition.StructureDefinitionKind.Resource)
                {
                    // [WMR 20190131] Fixed
                    var baseDef = await _generator.getBaseDefinition(expanded);
                    bool isDerivedFromResource = baseDef?.Url == ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Resource);
                    bool isDerivedFromDomainResource = baseDef?.Url == ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.DomainResource);
                    bool isDomainResource = expanded.Name == "DomainResource";

                    // [WMR 20190130] STU3
                    //if (rootElemName != "Resource")
                    //{
                    //    verified &= verifyBasePath(expandedElems[0], originalElems[0], "Resource");
                    //}

                    // [WMR 20190130] R4
                    // Root element base component always refers to self (.Base.Path = .Path)
                    verified &= verifyBasePath(expandedElems[0], originalElems[0], rootElemName);

                    if (isDerivedFromResource || isDerivedFromDomainResource && (expandedElems.Count > 4))
                    {
                        verified &= verifyBasePath(expandedElems[1], originalElems[1], "Resource.id");
                        verified &= verifyBasePath(expandedElems[2], originalElems[2], "Resource.meta");
                        verified &= verifyBasePath(expandedElems[3], originalElems[3], "Resource.implicitRules");
                        verified &= verifyBasePath(expandedElems[4], originalElems[4], "Resource.language");
                    }
                    var startIdx = 5;
                    if ((isDomainResource || isDerivedFromDomainResource) && (expandedElems.Count > 8))
                    {
                        verified &= verifyBasePath(expandedElems[5], originalElems[5], "DomainResource.text");
                        verified &= verifyBasePath(expandedElems[6], originalElems[6], "DomainResource.contained");
                        verified &= verifyBasePath(expandedElems[7], originalElems[7], "DomainResource.extension");
                        verified &= verifyBasePath(expandedElems[8], originalElems[8], "DomainResource.modifierExtension");
                        startIdx = 9;
                    }
                    if (!isDomainResource)
                    {
                        for (int i = startIdx; i < expandedElems.Count; i++)
                        {
                            var path = expandedElems[i].Path;
                            if (path.EndsWith(".id"))
                            {
                                verified &= verifyBasePath(expandedElems[i], originalElems[i], "Element.id");
                            }
                            else if (path.EndsWith(".extension"))
                            {
                                verified &= verifyBasePath(expandedElems[i], originalElems[i], "Element.extension");
                            }
                            else if (path.EndsWith(".modifierExtension"))
                            {
                                verified &= verifyBasePath(expandedElems[i], originalElems[i], "BackboneElement.modifierExtension");
                            }
                            else
                            {
                                verified &= verifyBasePath(expandedElems[i], originalElems[i], expandedElems[i].Path);
                            }
                        }
                    }
                }

                if (isConstraint)
                {
                    for (int i = 0; i < expandedElems.Count; i++)
                    {
                        if (originalElems[i].Base == null) { verified = false; Debug.WriteLine($"ORIGINAL: Path = {originalElems[i].Path}  => BASE IS MISSING"); }
                        if (expandedElems[i].Base == null) { verified = false; Debug.WriteLine($"EXPANDED: Path = {expandedElems[i].Path}  => BASE IS MISSING"); }
                    }
                }


            }
            return verified;
        }

        private static bool verifyBasePath(ElementDefinition elem, ElementDefinition orgElem, string path = "")
        {
            bool result = false;

            Debug.WriteLineIf(elem.Base == null, $"EXPANDED: Path = {elem.Path}  => BASE IS MISSING");
            Debug.WriteLineIf(orgElem.Base == null, "ORIGINAL: Path = {orgElem.Path}  => BASE IS MISSING");

            // R4: ElementDefinition.Base for newly introduced elements refers to self (.Base.Path == .Path)
            if (!string.IsNullOrEmpty(path))
            {
                // Assert.IsNotNull(elem.Base);
                // Assert.AreEqual(path, elem.Base.Path);

                // Assert.IsNotNull(orgElem.Base);
                // Assert.AreEqual(path, orgElem.Base.Path);

                result = elem.Base != null && path == elem.Base.Path;

                Debug.WriteLineIf(elem.Base != null && path != elem.Base.Path, $"EXPANDED: Path = {elem.Path} Base = {elem.Base?.Path} != {path} => INVALID BASE PATH");
                Debug.WriteLineIf(orgElem.Base != null && path != orgElem.Base.Path, $"ORIGINAL: Path = {orgElem.Path} Base = {orgElem.Base?.Path} != {path} => INVALID BASE PATH");
                Debug.Assert(!(orgElem.Base != null && path != orgElem.Base.Path));
            }
            // STU3: ElementDefinition.Base for newly introduced elements is empty
            //else
            //{
            //    // New resource element
            //    result = elem.Base == null;
            //    Debug.WriteLineIf(elem.Base != null, $"EXPANDED: Path = {elem.Path} Base = {elem.Base?.Path} != '' => BASE SHOULD BE NULL");
            //    Debug.WriteLineIf(orgElem.Base != null, $"ORIGINAL: Path = {orgElem.Path} Base = {orgElem.Base?.Path} != '' => BASE SHOULD BE NULL");

            //}

            Assert.IsTrue(result);

            return result;
        }

        // [WMR 20161207] NEW
        // Verify reslicing order
        [TestMethod]
        public async Tasks.Task TestReslicingOrder()
        {
            var dirSource = new DirectorySource("TestData/validation");
            var sd = await dirSource.FindStructureDefinitionAsync("http://example.com/StructureDefinition/patient-telecom-reslice-ek");
            Assert.IsNotNull(sd);

            //Patient.telecom : ''
            //Patient.telecom : 'phone'
            //Patient.telecom : 'email'
            //Patient.telecom : 'email/home'
            //Patient.telecom : 'email/work'
            //Patient.telecom : 'other'
            //Patient.telecom : 'other/home'
            //Patient.telecom : 'other/work'

            // Verify original differential - defines reslicing
            Debug.Print("Verify differential...");
            var diffNav = ElementDefinitionNavigator.ForDifferential(sd);
            assertPatientTelecomReslice(diffNav);

            var (_, expanded) = await generateSnapshotAndCompare(sd);

            Debug.Print("Verify snapshot...");
            var snapNav = ElementDefinitionNavigator.ForSnapshot(expanded);
            assertPatientTelecomReslice(snapNav);
        }

        private void assertPatientTelecomReslice(ElementDefinitionNavigator nav)
        {
            Assert.IsTrue(nav.MoveToFirstChild());  // Patient

            if (ElementDefinitionNavigator.IsRootPath(nav.Path))
            {
                Assert.IsTrue(nav.MoveToChild("telecom"));
            }

            var bm = nav.Bookmark();
            do
            {
                Debug.Print($"{nav.Path} : '{nav.Current.SliceName}'");
            } while (nav.MoveToNext("telecom"));
            nav.ReturnToBookmark(bm);

            // Patient.telecom - slicing introduction
            Assert.IsTrue(nav.Path == "Patient.telecom");
            Assert.IsNotNull(nav.Current.Slicing);

            // Patient.telecom - slice "phone"
            Assert.IsTrue(nav.MoveToNext());
            Assert.IsTrue(nav.Path == "Patient.telecom");
            Assert.IsTrue(nav.Current.SliceName == "phone");

            // Patient.telecom - slice "email"
            Assert.IsTrue(nav.MoveToNext());
            Assert.IsTrue(nav.Path == "Patient.telecom");
            Assert.IsTrue(nav.Current.SliceName == "email");

            // Patient.telecom - reslice "email/home"
            Assert.IsTrue(nav.MoveToNext());
            Assert.IsTrue(nav.Path == "Patient.telecom");
            Assert.IsTrue(nav.Current.SliceName == "email/home");

            // Patient.telecom - reslice "email/work"
            Assert.IsTrue(nav.MoveToNext());
            Assert.IsTrue(nav.Path == "Patient.telecom");
            Assert.IsTrue(nav.Current.SliceName == "email/work");

            // Patient.telecom - slice "other"
            Assert.IsTrue(nav.MoveToNext());
            Assert.IsTrue(nav.Path == "Patient.telecom");
            Assert.IsTrue(nav.Current.SliceName == "other");

            // Patient.telecom - reslice "other/home"
            Assert.IsTrue(nav.MoveToNext());
            Assert.IsTrue(nav.Path == "Patient.telecom");
            Assert.IsTrue(nav.Current.SliceName == "other/home");

            // Patient.telecom - reslice "other/work"
            Assert.IsTrue(nav.MoveToNext());
            Assert.IsTrue(nav.Path == "Patient.telecom");
            Assert.IsTrue(nav.Current.SliceName == "other/work");
        }


        // [WMR 20161207] DEBUGGING
        // List all complex extensions that are available in the TestData folder

        // http://hl7.org/fhir/StructureDefinition/cqif-basic-codeSystem : 'TestData/snapshot-test/extensions\extension-cqif-basic-codesystem.xml'
        // http://hl7.org/fhir/StructureDefinition/cqif-basic-contributor : 'TestData/snapshot-test/extensions\extension-cqif-basic-contributor.xml'
        // http://hl7.org/fhir/StructureDefinition/cqif-basic-data : 'TestData/snapshot-test/extensions\extension-cqif-basic-data.xml'
        // http://hl7.org/fhir/StructureDefinition/cqif-basic-guidance-action : 'TestData/snapshot-test/extensions\extension-cqif-basic-guidance-action.xml'
        // http://hl7.org/fhir/StructureDefinition/cqif-basic-guidance-trigger : 'TestData/snapshot-test/extensions\extension-cqif-basic-guidance-trigger.xml'
        // http://hl7.org/fhir/StructureDefinition/cqif-basic-library : 'TestData/snapshot-test/extensions\extension-cqif-basic-library.canonical.xml'
        // http://hl7.org/fhir/StructureDefinition/cqif-basic-model : 'TestData/snapshot-test/extensions\extension-cqif-basic-model.xml'
        // http://hl7.org/fhir/StructureDefinition/cqif-basic-parameter : 'TestData/snapshot-test/extensions\extension-cqif-basic-parameter.xml'
        // http://hl7.org/fhir/StructureDefinition/cqif-basic-relatedResource : 'TestData/snapshot-test/extensions\extension-cqif-basic-relatedresource.xml'
        // http://hl7.org/fhir/StructureDefinition/cqif-basic-valueSet : 'TestData/snapshot-test/extensions\extension-cqif-basic-valueset.xml'
        // http://hl7.org/fhir/StructureDefinition/encounter-relatedCondition : 'TestData/snapshot-test/extensions\extension-encounter-relatedcondition.xml'
        // http://hl7.org/fhir/StructureDefinition/family-member-history-genetics-parent : 'TestData/snapshot-test/extensions\extension-family-member-history-genetics-parent.xml'
        // http://hl7.org/fhir/StructureDefinition/gao-extension-item : 'TestData/snapshot-test/extensions\extension-gao-extension-item.canonical.xml'
        // http://hl7.org/fhir/StructureDefinition/goal-target : 'TestData/snapshot-test/extensions\extension-goal-target.xml'
        // http://hl7.org/fhir/StructureDefinition/patient-clinicalTrial : 'TestData/snapshot-test/extensions\extension-patient-clinicaltrial.xml'
        // http://hl7.org/fhir/StructureDefinition/patient-nationality : 'TestData/snapshot-test/extensions\extension-patient-nationality.xml'
        // http://hl7.org/fhir/StructureDefinition/qicore-adverseevent-cause : 'TestData/snapshot-test/extensions\extension-qicore-adverseevent-cause.xml'
        // http://hl7.org/fhir/StructureDefinition/questionnaire-enableWhen : 'TestData/snapshot-test/extensions\extension-questionnaire-enablewhen.xml'

        [TestMethod]
        public async Tasks.Task FindComplexTestExtensions()
        {
            //Assert.Fail("CHANGE all files in TestData/snapshot-test/extensions. The FhirVersion should be 4.0.0");
            Debug.WriteLine("Complex extension in TestData folder:");
            var dirSource = new DirectorySource("TestData/snapshot-test/extensions");
            var uris = dirSource.ListResourceUris(ResourceType.StructureDefinition);
            foreach (var uri in uris)
            {
                var sd = await dirSource.FindStructureDefinitionAsync(uri);
                if (sd.IsExtension)
                {
                    if (sd.Differential.Element.Any(e => e.Path.StartsWith("Extension.extension.", StringComparison.Ordinal)))
                    {
                        // var orgInfo = sd.Annotation<OriginAnnotation>();
                        // Debug.WriteLine($"{uri} : '{orgInfo?.Origin}'");
                        Debug.WriteLine($"{uri} : '{sd.GetOrigin()}'");
                    }
                }
            }
        }

        // Ewout: type slices cannot contain renamed elements!
        private static StructureDefinition ObservationTypeSliceProfile => new()
        {
            Type = FHIRAllTypes.Observation.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation),
            Name = "MyTestObservation",
            Url = "http://example.org/fhir/StructureDefinition/MyTestObservation",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Observation.value[x]")
                    {
                        Slicing = new ElementDefinition.SlicingComponent()
                        {
                            // Discriminator = new string[] { "@type" },
                            Discriminator = new ElementDefinition.DiscriminatorComponent[]
                                { new ElementDefinition.DiscriminatorComponent
                                    { Type = ElementDefinition.DiscriminatorType.Type }
                                }.ToList(),
                            Ordered = false,
                            Rules = ElementDefinition.SlicingRules.Open
                        }
                    }
                    ,new ElementDefinition("Observation.value[x]")
                    {
#if !GENERATE_MISSING_TYPE_SLICE_NAMES
                        // [WMR 20190828] SnapshotGenerator generates missing sliceNames for type slices
                        SliceName = "valueString",
#endif

                        Type = new List<ElementDefinition.TypeRefComponent>()
                        {
                            new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.String.GetLiteral() }
                        }
                    }
                }
            }
        };

        [Conditional("DEBUG")]
        private void dumpElements(IEnumerable<ElementDefinition> elements, string header = null) => dumpElements(elements.ToList(), header);

        [Conditional("DEBUG")]
        private void dumpElements(List<ElementDefinition> elements, string header = null)
        {
            Debug.WriteLineIf(!string.IsNullOrEmpty(header), header);
            for (int i = 0; i < elements.Count; i++)
            {
                var elem = elements[i];
                Debug.Write(elem.Path);
                Debug.WriteIf(elem.Path != null, " '" + elem.SliceName + "'");
                if (elem.Slicing != null)
                {
                    Debug.Write(" => sliced on: " + string.Join(" | ", elem.Slicing.Discriminator.Select(p => p?.Path)));
                }
                Debug.WriteLine("");
            }
        }

        [TestMethod]
        public async Tasks.Task TestTypeSlicing()
        {
            // Create a profile with a type slice: { value[x], value[x] : String }
            var profile = ObservationTypeSliceProfile;

            var resolver = new InMemoryResourceResolver(profile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(profile);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

#if GENERATE_MISSING_TYPE_SLICE_NAMES
            // Expecting informational messages about generated slice names
            dumpOutcome(_generator.Outcome);
#endif

            expanded.Snapshot.Element.Where(e => e.Path.StartsWith("Observation.value")).Dump("[1] Observation.value slice:");

            var nav = new ElementDefinitionNavigator(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());
            Assert.AreEqual(nav.Path, "Observation");
            Assert.IsTrue(nav.MoveToChild("value[x]"));
            Assert.IsNotNull(nav.Current.Slicing);
            Assert.IsTrue(nav.MoveToNext());
            //Assert.AreEqual(nav.PathName, "valueString"); // NOT Normalized
            Assert.AreEqual(nav.PathName, "value[x]"); // Normalized
            Assert.AreEqual(nav.Current.Type.FirstOrDefault().Code, FHIRAllTypes.String.GetLiteral());

            // Add an additional type slice: { value[x], value[x] : String, value[x] : CodeableConcept }
            profile.Differential.Element.Add(
                new ElementDefinition("Observation.value[x]")
                {
#if !GENERATE_MISSING_TYPE_SLICE_NAMES
                    // [WMR 20190828] SnapshotGenerator generates missing sliceNames for type slices
                    SliceName = "valueCodeableConcept",
#endif
                    Type = new List<ElementDefinition.TypeRefComponent>()
                    {
                        new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.CodeableConcept.GetLiteral() }
                    }
                }
            );

            (_, expanded) = await generateSnapshotAndCompare(profile);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            expanded.Snapshot.Element.Where(e => e.Path.StartsWith("Observation.value")).Dump("[2] Observation.value slice:");

            nav = new ElementDefinitionNavigator(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());
            Assert.AreEqual(nav.Path, "Observation");
            Assert.IsTrue(nav.MoveToChild("value[x]"));
            Assert.IsTrue(nav.MoveToNext());
            Assert.AreEqual(nav.PathName, "value[x]"); // valueString
            Assert.AreEqual(nav.Current.Type.FirstOrDefault()?.Code, FHIRAllTypes.String.GetLiteral());
            Assert.IsTrue(nav.MoveToNext());
            Assert.AreEqual(nav.PathName, "value[x]"); // valueCodeableConcept
            Assert.AreEqual(nav.Current.Type.FirstOrDefault()?.Code, FHIRAllTypes.CodeableConcept.GetLiteral());
        }

        [TestMethod]
        public async Tasks.Task TestMissingDifferential()
        {
            // Create a profile without a differential
            var profile = ObservationTypeSliceProfile;
            profile.Differential = null;

            var resolver = new InMemoryResourceResolver(profile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            var (_, expanded) = await generateSnapshotAndCompare(profile);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            expanded.Snapshot.Element.Dump();
        }

        [TestMethod]
        public async Tasks.Task TestUnresolvedBaseProfile()
        {
            // Create a profile with an unresolved base profile reference
            var profile = ObservationTypeSliceProfile;
            profile.BaseDefinition = "http://example.org/fhir/StructureDefinition/missing";

            var resolver = new InMemoryResourceResolver(profile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(profile);
            Assert.IsNotNull(expanded);
            Assert.IsFalse(expanded.HasSnapshot);
            var outcome = _generator.Outcome;
            Assert.IsNotNull(outcome);
            Assert.IsNotNull(outcome.Issue);
            Assert.AreEqual(outcome.Issue.Count, 1);
            assertIssue(outcome.Issue[0], Issue.UNAVAILABLE_REFERENCED_PROFILE, profile.BaseDefinition);
        }

        private static StructureDefinition ObservationTypeResliceProfile => new()
        {
            Type = FHIRAllTypes.Observation.GetLiteral(),
            BaseDefinition = ObservationTypeSliceProfile.Url,
            Name = "MyDerivedTestObservation",
            Url = "http://example.org/fhir/StructureDefinition/MyDerivedTestObservation",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Observation.value[x]")
                    {
                        Slicing = new ElementDefinition.SlicingComponent()
                        {
                            // Discriminator = new string[] { "@type" },
                            Discriminator = new ElementDefinition.DiscriminatorComponent[]
                                { new ElementDefinition.DiscriminatorComponent
                                    { Type = ElementDefinition.DiscriminatorType.Type }
                                }.ToList(),
                            Ordered = false,
                            Rules = ElementDefinition.SlicingRules.Open
                        }
                    }
                    // Constraint on existing type slice value[x] : String
                    ,new ElementDefinition("Observation.value[x]")
                    {
#if !GENERATE_MISSING_TYPE_SLICE_NAMES
                        // [WMR 20190828] SnapshotGenerator generates missing sliceNames for type slices
                        SliceName = "valueString",
#endif

                        Max = "1", // New constraint
                        Type = new List<ElementDefinition.TypeRefComponent>()
                        {
                            new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.String.GetLiteral() }
                        }
                    }
                    
                    // Remove existing type slice value[x]: CodeableConcept

                    // Add a new type slice value[x]: Integer
                    ,new ElementDefinition("Observation.value[x]")
                    {
#if !GENERATE_MISSING_TYPE_SLICE_NAMES
                        // [WMR 20190828] SnapshotGenerator generates missing sliceNames for type slices
                        SliceName = "valueInteger",
#endif

                        Type = new List<ElementDefinition.TypeRefComponent>()
                        {
                            new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.Integer.GetLiteral() }
                        }
                    },
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestTypeReslicing()
        {
            // Create a derived profile from a base profile with a type slice
            var profile = ObservationTypeResliceProfile;
            var baseProfile = ObservationTypeSliceProfile;

            var resources = new Resource[] { profile, baseProfile };
            var resolver = new InMemoryResourceResolver(resources);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(profile);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

#if GENERATE_MISSING_TYPE_SLICE_NAMES
            // Expecting informational messages about generated slice names
            dumpOutcome(_generator.Outcome);
#endif

            expanded.Snapshot.Element.Where(e => e.Path.StartsWith("Observation.value")).Dump("[1] Observation.value reslice:");

            var nav = new ElementDefinitionNavigator(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());
            Assert.AreEqual(nav.Path, "Observation");
            Assert.IsTrue(nav.MoveToChild("value[x]"));
            Assert.IsTrue(nav.MoveToNext());
            Assert.AreEqual(nav.PathName, "value[x]"); // valueString
            Assert.AreEqual(nav.Current.Type.FirstOrDefault()?.Code, FHIRAllTypes.String.GetLiteral());
            // Derived profile REMOVES existing CodeableConcept type slice and introduces a new Integer type slice
            // Note: special rules for element types allow removal of inherited collection items
            Assert.IsTrue(nav.MoveToNext());
            Assert.AreEqual(nav.PathName, "value[x]"); // valueCodeableConcept
            Assert.AreEqual(nav.Current.Type.FirstOrDefault()?.Code, FHIRAllTypes.Integer.GetLiteral());
        }

        // Choice type constraint, with element renaming
        private static StructureDefinition ObservationTypeConstraintProfile => new()
        {
            Type = FHIRAllTypes.Observation.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation),
            Name = "MyTestObservation",
            Url = "http://example.org/fhir/StructureDefinition/MyTestObservation",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    // No slicing introduction
                    // STU3: Only single element is allowed (this is NOT a slice!)
                    // Element is renamed
                    new ElementDefinition("Observation.valueString")
                    {
                        Type = new List<ElementDefinition.TypeRefComponent>()
                        {
                            new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.String.GetLiteral() }
                        }
                    }
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestChoiceTypeConstraint()
        {
            // Create a profile with a choice type constraint: value[x] => valueString
            var profile = ObservationTypeConstraintProfile;

            var resolver = new InMemoryResourceResolver(profile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(profile);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            expanded.Snapshot.Element.Where(e => e.Path.StartsWith("Observation.value")).Dump("Observation.value choice type constraint:");

            var nav = new ElementDefinitionNavigator(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());
            Assert.AreEqual(nav.Path, "Observation");

            // [WMR 20190204] STU3: "value[x]" should be renamed to valueString in snapshot
            //Assert.IsFalse(nav.MoveToChild("value[x]")); // 
            //Assert.IsTrue(nav.MoveToChild("valueString"));
            // [WMR 20190204] R4: snapshot should include both "value[x]" and "valueString"
            Assert.IsTrue(nav.MoveToChild("value[x]"));
#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.MoveToNextSlice("valueString"));
#else
            Assert.IsTrue(nav.MoveToNext("valueString"));
#endif

            Assert.IsNull(nav.Current.Slicing);
            Assert.AreEqual(nav.Current.Type.FirstOrDefault().Code, FHIRAllTypes.String.GetLiteral());
        }

        //[Ignore("TODO: Fix choice type constraints for R4")]
        [TestMethod]
        public async Tasks.Task TestInvalidChoiceTypeConstraints()
        {
            // Create a profile with multiple choice type constraint: value[x] => { valueString, valueInteger }
            // STU3: multiple renamed choice type constraints are invalid
            // R4: multiple renamed choice type constraints are allowed
            var profile = ObservationTypeConstraintProfile;
            profile.Differential.Element.Add(
                    new ElementDefinition("Observation.valueInteger")
                    {
                        Type = new List<ElementDefinition.TypeRefComponent>()
                        {
                            new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.Integer.GetLiteral() }
                        }
                    }
            );

            var resolver = new InMemoryResourceResolver(profile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(profile);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            expanded.Snapshot.Element.Where(e => e.Path.StartsWith("Observation.value")).Dump("Observation.value choice type constraint:");
            var outcome = _generator.Outcome;
            dumpOutcome(outcome);

            var nav = new ElementDefinitionNavigator(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());
            Assert.AreEqual(nav.Path, "Observation");

            // [WMR 20190204] STU3: "value[x]" should be renamed to valueString in snapshot
            //Assert.IsFalse(nav.MoveToChild("value[x]"));
            //Assert.IsTrue(nav.MoveToChild("valueString"));
            // [WMR 20190204] R4: snapshot should include "value[x]" "valueString", and "valueInteger"
            Assert.IsTrue(nav.MoveToChild("value[x]"));
#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.MoveToNextSlice("valueString"));
#else
            Assert.IsTrue(nav.MoveToNext("valueString"));
#endif

            Assert.IsNull(nav.Current.Slicing);
            Assert.AreEqual(nav.Current.Type.FirstOrDefault().Code, FHIRAllTypes.String.GetLiteral());

#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.MoveToNextSlice("valueInteger"));
#else
            Assert.IsTrue(nav.MoveToNext("valueInteger"));
#endif
            Assert.IsNull(nav.Current.Slicing);
            Assert.AreEqual(nav.Current.Type.FirstOrDefault().Code, FHIRAllTypes.Integer.GetLiteral());

            // [WMR 20190211] STU3: Disallow multiple renamed choice type constraints
            //Assert.IsNotNull(outcome);
            //Assert.AreEqual(1, outcome.Issue.Count);
            //assertIssue(outcome.Issue[0], SnapshotGenerator.PROFILE_ELEMENTDEF_INVALID_CHOICE_CONSTRAINT);

            // [WMR 20190211] R4: Allow multiple renamed choice type constraints
            Assert.IsNull(outcome);
        }

        // [WMR 20190204] #824
        // Support R4 choice type shortcut syntax
        // Example: http://hl7.org/fhir/bmi.profile.xml.html

        // Generic type slicing:
        // - Slicing Introduction: Slicing { Discriminator = { "Type", "$this" } }
        // - Named slices:
        //     - Limit the list of allowed types (defined by intro element)
        //     - Specify additional constraints for specific type(s)
        //       Override common constraints inherited from slicing introduction
        //
        // Choice type elements:
        // - Constraints on "value[x]" apply to all allowed types
        //   - Constraints on "value[x].type" limit the list of allowed types
        //     Note: cannot introduce new (incompatible) types!
        //   - Slicing component is optional, but allowed
        //     If present, first Discriminator in list should be { "Type", "$this" }
        //     Note: derived profiles can reslice and add additional discriminator components
        // - Shortcut: allow constraint on single type w/o slicing introduction
        //     - Rename element, e.g. "value[x]" => "valueString"
        //     - Element.type is optional
        //       Matching type is implied from element name
        //       e.g. "valueString" specifies constraints for type choice "string"
        //     - Allow multiple, e.g. "valueString" and "valueBoolean"
        // - Also allow regular type slicing (with slicing intro)
        //     - e.g. named slice with constraints for a subset of allowed types
        //     - Rename element path if constraints apply to *single* type
        //
        // Snapshot component in R4:
        // - Always include original choice type element (e.g. "value[x]")
        // - Also include concrete type slices and/or renamed single type constraints
        //
        // Snapshot component in STU3:
        // - Choice type element is renamed if constrained to a single type
        //   e.g. snapshot only includes "valueString", but not "value[x]"
        // - Otherwise, if multiple types are allowed, do not rename the element(s)
        //   Generate a unique slice name that includes the renamed element name
        //   e.g. choice type constrained to string and boolean
        //   => snapshot includes "value[x]" (slicing intro), "value[x]:valueString" and "value[x]:valueBoolean"

        [TestMethod]
        public async Tasks.Task TestChoiceTypeCommonConstraint()
        {
            var obsProfile = new StructureDefinition()
            {
                Type = FHIRAllTypes.Observation.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation),
                Name = "ChoiceTypeObservation",
                Url = "http://example.org/fhir/StructureDefinition/ChoiceTypeObservation",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        // Specify common constraints that apply to all choice types
                        new ElementDefinition("Observation.value[x]")
                        {
                            Min = 1,
                            // Limit the list of allowed types
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent() { Code = "string" },
                                new ElementDefinition.TypeRefComponent() { Code = "boolean" },
                            }
                        },
                    }
                }
            };

            var resolver = new InMemoryResourceResolver(obsProfile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            var generator = _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(obsProfile);

            dumpOutcome(generator.Outcome);
            dumpBaseElems(expanded.Snapshot.Element);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            // Should be allowed
            var outcome = generator.Outcome;
            Assert.IsNull(outcome);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.JumpToFirst("Observation.value[x]"));
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual(2, nav.Current.Type.Count);
            Assert.AreEqual("string", nav.Current.Type[0].Code);
            Assert.AreEqual("boolean", nav.Current.Type[1].Code);
        }

        [TestMethod]

        public async Tasks.Task TestChoiceTypeWithTypeSlice()
        {
            var obsProfile = new StructureDefinition()
            {
                Type = FHIRAllTypes.Observation.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation),
                Name = "ChoiceTypeObservation",
                Url = "http://example.org/fhir/StructureDefinition/ChoiceTypeObservation",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        // Type slicing introduction
                        new ElementDefinition("Observation.value[x]")
                        {
                            Slicing = new ElementDefinition.SlicingComponent()
                            {
                                Discriminator = new List<ElementDefinition.DiscriminatorComponent>()
                                {
                                    ElementDefinition.DiscriminatorComponent.ForTypeSlice()
                                }
                            },
                        },
                        // Concrete type slice
                        new ElementDefinition("Observation.value[x]")
                        {
                            SliceName = "FirstTypeSlice",
                            MaxLength = 100,
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.String.GetLiteral() }
                            }
                        },
                    }
                }
            };

            var resolver = new InMemoryResourceResolver(obsProfile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            var generator = _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(obsProfile);

            dumpOutcome(generator.Outcome);
            dumpBaseElems(expanded.Snapshot.Element);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            // Should be allowed
            var outcome = generator.Outcome;
            Assert.IsNull(outcome);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);

            Assert.IsTrue(nav.JumpToFirst("Observation.value[x]"));
            Assert.IsNull(nav.Current.MaxLengthElement);
            Assert.AreNotEqual(1, nav.Current.Type.Count);

            Assert.IsTrue(nav.MoveToNextSlice());
            Assert.AreEqual("value[x]", nav.PathName);
            Assert.AreEqual("FirstTypeSlice", nav.Current.SliceName);
            Assert.AreEqual(100, nav.Current.MaxLength);
            Assert.AreEqual(1, nav.Current.Type.Count);
        }

        [TestMethod]

        public async Tasks.Task TestChoiceTypeSingleTypeConstraint()
        {
            var obsProfile = new StructureDefinition()
            {
                Type = FHIRAllTypes.Observation.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation),
                Name = "ChoiceTypeObservation",
                Url = "http://example.org/fhir/StructureDefinition/ChoiceTypeObservation",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        // Type-specific constraint
                        new ElementDefinition("Observation.valueString")
                        {
                            MaxLength = 100
                        },
                    }
                }
            };

            var resolver = new InMemoryResourceResolver(obsProfile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            var generator = _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(obsProfile);

            dumpOutcome(generator.Outcome);
            dumpBaseElems(expanded.Snapshot.Element);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            // Should be allowed
            var outcome = generator.Outcome;
            Assert.IsNull(outcome);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);

            Assert.IsTrue(nav.JumpToFirst("Observation.value[x]"));
            // Verify: MaxLength constraint only applies to valueString
            Assert.IsNull(nav.Current.MaxLengthElement);
            // Verify: type-specific constraint on valueString does NOT limit the list of allowable types
            Assert.AreNotEqual(1, nav.Current.Type.Count);

#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.MoveToNextSlice("valueString"));
#else
            Assert.IsTrue(nav.MoveToNext("valueString"));
#endif
            Assert.AreEqual(100, nav.Current.MaxLength);
        }

        [TestMethod]

        public async Tasks.Task TestChoiceTypeMultipleTypeConstraints()
        {
            var obsProfile = new StructureDefinition()
            {
                Type = FHIRAllTypes.Observation.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation),
                Name = "ChoiceTypeObservation",
                Url = "http://example.org/fhir/StructureDefinition/ChoiceTypeObservation",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Observation.valueString")
                        {
                            MaxLength = 100
                        },
                        new ElementDefinition("Observation.valueInteger")
                        {
                            MinValue = new Integer(0)
                        },
                    }
                }
            };

            var resolver = new InMemoryResourceResolver(obsProfile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            var generator = _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(obsProfile);

            dumpOutcome(generator.Outcome);
            dumpBaseElems(expanded.Snapshot.Element);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            // Should be allowed
            // Specifically, should not return "PROFILE_ELEMENTDEF_INVALID_CHOICE_CONSTRAINT" issues (STU3 only)
            var outcome = generator.Outcome;
            Assert.IsNull(outcome);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);

            Assert.IsTrue(nav.JumpToFirst("Observation.value[x]"));
            // Verify: MaxLength constraint only applies to valueString
            Assert.IsNull(nav.Current.MaxLengthElement);
            // Verify: MinValue constraint only applies to valueInteger
            Assert.IsNull(nav.Current.MinValue);
            // Verify: type-specific constraint on valueString does NOT limit the list of allowable types
            Assert.AreNotEqual(1, nav.Current.Type.Count);

#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.MoveToNextSlice("valueString"));
#else
            Assert.IsTrue(nav.MoveToNext("valueString"));
#endif
            Assert.AreEqual(100, nav.Current.MaxLength);

#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.MoveToNextSlice("valueInteger"));
#else
            Assert.IsTrue(nav.MoveToNext("valueInteger"));
#endif
            Assert.IsTrue(nav.Current.MinValue is Integer i && i.Value == 0);
        }

        private static StructureDefinition ClosedExtensionSliceObservationProfile => new StructureDefinition()
        {
            Type = FHIRAllTypes.Observation.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation),
            Name = "MyTestObservation",
            Url = "http://example.org/fhir/StructureDefinition/MyTestObservation",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Observation.extension")
                    {
                        Slicing = new ElementDefinition.SlicingComponent()
                        {
                            Rules = ElementDefinition.SlicingRules.Closed
                        }
                    }
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestEmptyClosedExtensionSlice()
        {
            var profile = ClosedExtensionSliceObservationProfile;

            var resolver = new InMemoryResourceResolver(profile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(profile);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            // dumpElements(expanded.Snapshot.Element.Where(e => e.Path.StartsWith("Observation.extension")), "Observation.extension constraint:");
            var outcome = _generator.Outcome;
            dumpOutcome(outcome);

            var elem = expanded.Snapshot.Element.Find(e => e.Path == "Observation.extension");
            Assert.IsNotNull(elem);
            Assert.IsNotNull(elem.Slicing);
            Assert.AreEqual(ElementDefinition.SlicingRules.Closed, elem.Slicing.Rules);
        }

        [TestMethod]
        public async Tasks.Task TestSlicingEntryWithChilren()
        {
            var sd = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/StructureDefinition/DocumentComposition");
            Assert.IsNotNull(sd);

            // dumpReferences(sd);

            var (_, expanded) = await generateSnapshotAndCompare(sd);

            dumpOutcome(_generator.Outcome);
            expanded.Snapshot.Element.Dump();

            // Verify that the snapshot includes the merged children of the slice entry element
            var verifier = new ElementVerifier(expanded, _settings);
            verifier.VerifyElement("Composition.section", null);
            verifier.AssertSlicing("code", ElementDefinition.SlicingRules.Open, false);
            verifier.VerifyElement("Composition.section.title", null);
            verifier.VerifyElement("Composition.section.code", null);
            Assert.IsNotNull(verifier.CurrentElement.Binding);
            Assert.AreEqual(BindingStrength.Required, verifier.CurrentElement.Binding.Strength);
            Assert.AreEqual("http://example.org/ValueSet/SectionTitles", verifier.CurrentElement.Binding.ValueSet);
        }

        [TestMethod]
        public async Tasks.Task TestObservationProfileWithExtensions() => await testObservationProfileWithExtensions(false);

        [TestMethod]
        public async Tasks.Task TestObservationProfileWithExtensions_ExpandAll() => await testObservationProfileWithExtensions(true);

        private async Tasks.Task testObservationProfileWithExtensions(bool expandAll)
        {
            // Same as TestObservationProfileWithExtensions, but with full expansion of all complex elements (inc. extensions!)

            // var obs = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyCustomObservation");
            var obs = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyCustomObservation3");
            Assert.IsNotNull(obs);

            _generator = new SnapshotGenerator(_testResolver, _settings);
            _generator.PrepareElement += elementHandler;
            List<ElementDefinition> elems;
            try
            {
                var (_, expanded) = await generateSnapshotAndCompare(obs);

                dumpOutcome(_generator.Outcome);

                elems = expanded.Snapshot.Element;
                elems.Dump();
                Debug.WriteLine($"Default snapshot: {elems.Count} elements");
                dumpBaseElems(elems);
                var issues = _generator.Outcome?.Issue ?? new List<OperationOutcome.IssueComponent>();

                // [WMR 20180115] NEW - Use alternative (iterative) approach for full expansion
                if (expandAll)
                {
                    elems = await fullyExpand(elems, issues);
                    Debug.WriteLine($"Fully expanded: {elems.Count} elements");
                }
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }

            // Verify that the snapshot contains three extension elements 
            var obsExtensions = elems.Where(e => e.Path == "Observation.extension").ToList();
            Assert.IsNotNull(obsExtensions);
            Assert.AreEqual(4, obsExtensions.Count); // 1 extension slice + 3 extensions

            var extSliceElem = obsExtensions[0];
            Assert.IsNotNull(extSliceElem);
            Assert.IsNotNull(extSliceElem.Slicing);
            Assert.AreEqual("url", extSliceElem.Slicing.Discriminator.FirstOrDefault().Path);

            var labelExtElem = obsExtensions[1];
            Assert.IsNotNull(labelExtElem);
            Assert.AreEqual(@"http://example.org/fhir/StructureDefinition/ObservationLabelExtension", labelExtElem.Type.FirstOrDefault().Profile.FirstOrDefault());

            var locationExtElem = obsExtensions[2];
            Assert.IsNotNull(locationExtElem);
            Assert.AreEqual(@"http://example.org/fhir/StructureDefinition/ObservationLocationExtension", locationExtElem.Type.FirstOrDefault().Profile.FirstOrDefault());

            var otherExtElem = obsExtensions[3];
            Assert.IsNotNull(otherExtElem);
            Assert.AreEqual(@"http://example.org/fhir/StructureDefinition/SomeOtherExtension", otherExtElem.Type.FirstOrDefault().Profile.FirstOrDefault());

            var labelExt = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/ObservationLabelExtension");
            Assert.IsNotNull(labelExt);
            if (expandAll) { Assert.AreEqual(true, labelExt.HasSnapshot); }

            var locationExt = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/ObservationLocationExtension");
            Assert.IsNotNull(locationExt);
            if (expandAll) { Assert.AreEqual(true, locationExt.HasSnapshot); }

            // Third extension element maps to an unresolved extension definition
            var otherExt = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/SomeOtherExtension");
            Assert.IsNull(otherExt);

            // Now verify the snapshot
            // First two extension elements should have been merged from the snapshot root Extension element of the associated extension definition 
            var coreExtension = await _testResolver.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.Extension);
            Assert.IsNotNull(coreExtension);
            Assert.IsTrue(coreExtension.HasSnapshot);
            var coreExtensionRootElem = coreExtension.Snapshot.Element[0];

            var labelExtRootElem = labelExt.Differential.Element[0];
            Assert.AreEqual(1, labelExtElem.Min);                                           // Explicit Observation profile constraint
            Assert.AreEqual(labelExtRootElem.Max, labelExtElem.Max);                        // Inherited from external ObservationLabelExtension root element
            // [WMR 20181212] R4 - Definition type changed from string to markdown
            Assert.IsTrue(labelExtElem.DefinitionElement.IsExactly(coreExtensionRootElem.DefinitionElement)); // Inherited from Observation.extension base element
            // [WMR 20181212] R4 - Comment type changed from string to markdown
            Assert.IsTrue(labelExtElem.CommentElement.IsExactly(labelExtRootElem.CommentElement));            // Inherited from external ObservationLabelExtension root element
            verifyProfileExtensionBaseElement(labelExtElem);

            var locationExtRootElem = locationExt.Differential.Element[0];
            Assert.AreEqual(0, locationExtElem.Min);                                        // Inherited from external ObservationLabelExtension root element
            Assert.AreEqual("1", locationExtElem.Max);                                      // Explicit Observation profile constraint
            // [WMR 20181212] R4 - Definition type changed from string to markdown
            Assert.IsTrue(locationExtElem.DefinitionElement.IsExactly(coreExtensionRootElem.DefinitionElement));  // Inherited from Observation.extension base element
            // [WMR 20181212] R4 - Comment type changed from string to markdown
            Assert.IsTrue(locationExtElem.CommentElement.IsExactly(locationExtRootElem.CommentElement));          // Inherited from external ObservationLocationExtension root element
            verifyProfileExtensionBaseElement(locationExtElem);

            // Last (unresolved) extension element should have been merged with Observation.extension
            var coreObservation = await _testResolver.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.Observation);
            Assert.IsNotNull(coreObservation);
            Assert.IsTrue(coreObservation.HasSnapshot);
            var coreObsExtensionElem = coreObservation.Snapshot.Element.FirstOrDefault(e => e.Path == "Observation.extension");
            Assert.IsNotNull(coreObsExtensionElem);
            Assert.AreEqual(1, otherExtElem.Min);                                           // Explicit Observation profile constraint
            Assert.AreEqual(coreObsExtensionElem.Max, otherExtElem.Max);                    // Inherited from Observation.extension base element
            // [WMR 20181212] R4 - Definition type changed from string to markdown
            Assert.IsTrue(otherExtElem.DefinitionElement.IsExactly(coreObsExtensionElem.DefinitionElement));  // Inherited from Observation.extension base element
            // [WMR 20181212] R4 - Comment type changed from string to markdown
            Assert.IsTrue(otherExtElem.CommentElement.IsExactly(coreObsExtensionElem.CommentElement));    // Inherited from Observation.extension base element
            verifyProfileExtensionBaseElement(coreObsExtensionElem);
        }

        private void verifyProfileExtensionBaseElement(ElementDefinition extElem)
        {
            var baseElem = extElem.Annotation<BaseDefAnnotation>().BaseElementDefinition;
            Assert.IsNotNull(baseElem);
            Assert.AreEqual(baseElem.Short, extElem.Short);
            // [WMR 20181212] R4 - Definition type changed from string to markdown
            Assert.IsTrue(extElem.DefinitionElement.IsExactly(baseElem.DefinitionElement));
            // [WMR 20181212] R4 - Comment type changed from string to markdown
            Assert.IsTrue(extElem.CommentElement.IsExactly(baseElem.CommentElement));
            Assert.IsTrue(baseElem.Alias.SequenceEqual(extElem.Alias));
        }

#if false
        // [WMR 20170213] New - issue reported by Marten - cannot slice Organization.type ?
        // Specifically, snapshot generator drops the slicing component from the slice entry element
        // Explanation: Organization.type is not a list (max = 1) and not a choice type => slicing is not allowed!
        [TestMethod]
        public async Tasks.Task TestOrganizationTypeSlice()
        {
            var org = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MySlicedOrganization");
            Assert.IsNotNull(org);

            StructureDefinition expanded;
            _generator = new SnapshotGenerator(_testResolver, _settings);
            _generator.PrepareElement += elementHandler;
            try
            {
                generateSnapshotAndCompare(org, out expanded);
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }

            //dumpOutcome(_generator.Outcome);

            //var elems = expanded.Snapshot.Element;
            //elems.Dump();
            //dumpBaseElems(elems);

            // TODO: Verify slice

        }
#endif

        // [WMR 2017024] NEW: Test for bug with snapshot expansion of ElementDefinition.Binding (reported by NHS)
        // If the diff constrains only Binding.Strength, then snapshot also contains only Binding.Strength - WRONG!
        // Expected: snapshot contains inherited properties from base, i.e. description, valueSetUri/valueSetReference
        [TestMethod]
        public async Tasks.Task TestElementBinding()
        {
            var sd = new StructureDefinition()
            {
                Type = FHIRAllTypes.Encounter.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Encounter),
                Name = "MyTestEncounter",
                Url = "http://example.org/fhir/StructureDefinition/MyTestEncounter",
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Encounter.type")
                        {

                            // Default binding on Encounter.type:
                            //
                            // <binding>
                            //   <strength value="example" />
                            //   <description value="The type of encounter" />
                            //   <valueSetReference>
                            //     <reference value="http://hl7.org/fhir/ValueSet/encounter-type" />
                            //   </valueSetReference>
                            // </binding>

                            Binding = new ElementDefinition.ElementDefinitionBindingComponent()
                            {
                                // Constrain strength from Example to Preferred
                                Strength = BindingStrength.Preferred
                            }
                        }
                    }

                }
            };

            var resolver = new InMemoryResourceResolver(sd);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            var (_, expanded) = await generateSnapshotAndCompare(sd);
            expanded.Should().NotBeNull();
            expanded.HasSnapshot.Should().BeTrue();

            var profileElem = expanded.Snapshot.Element.FirstOrDefault(e => e.Path == "Encounter.type");
            profileElem.Should().NotBeNull();
            var profileBinding = profileElem.Binding;
            profileBinding.Should().NotBeNull();

            profileBinding.Strength.Should().Be(BindingStrength.Preferred);

            var sdEncounter = await _testResolver.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.Encounter);
            Assert.IsNotNull(sdEncounter);
            Assert.IsTrue(sdEncounter.HasSnapshot);

            var baseElem = sdEncounter.Snapshot.Element.FirstOrDefault(e => e.Path == "Encounter.type");
            Assert.IsNotNull(baseElem);
            var baseBinding = baseElem.Binding;
            Assert.IsNotNull(baseBinding);

            Assert.AreEqual(BindingStrength.Example, baseBinding.Strength);

            baseBinding.Description.Should().BeEquivalentTo(profileBinding.Description);
            baseBinding.ValueSet.Should().BeEquivalentTo(profileBinding.ValueSet);
        }

        // [WMR 2017024] NEW: Snapshot generator should reject profile extensions mapped to a StructureDefinition that is not an Extension definition.
        // Reported by Thomas Tveit Rosenlund: https://simplifier.net/Velferdsteknologi2/FlagVFT (geoPositions)
        // Don't expand; emit outcome issue
        [TestMethod]
        public async Tasks.Task TestInvalidProfileExtensionTarget()
        {
            var sdLocation = new StructureDefinition()
            {
                Type = FHIRAllTypes.Location.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Location),
                Name = "MyTestLocation",
                Url = "http://example.org/fhir/StructureDefinition/MyTestLocation",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition()
                        {
                            Path = "Location.partOf",
                            Max = "0"
                        }
                    }
                }
            };

            var sdFlag = new StructureDefinition()
            {
                Type = FHIRAllTypes.Flag.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Flag),
                Name = "MyTestFlag",
                Url = "http://example.org/fhir/StructureDefinition/MyTestFlag",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Flag.extension")
                        {
                            Slicing = new ElementDefinition.SlicingComponent()
                            {
                                // Discriminator = new string[] { "url" },
                                Discriminator = new ElementDefinition.DiscriminatorComponent[]
                                { new ElementDefinition.DiscriminatorComponent
                                    { Type = ElementDefinition.DiscriminatorType.Value, Path = "url" }
                                }.ToList(),
                                Rules = ElementDefinition.SlicingRules.Open
                            }
                        },
                        new ElementDefinition("Flag.extension")
                        {
                            SliceName = "geopositions",
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.Extension.GetLiteral(),
                                    // INVALID - Map extension element to non-extension definition
                                    Profile = new string[] { sdLocation.Url }
                                }

                            }
                        }
                    }

                }
            };

            var resolver = new InMemoryResourceResolver(sdLocation, sdFlag);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);
            (_, var expanded) = await generateSnapshotAndCompare(sdFlag);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);
            expanded.Snapshot.Element.Dump();

            // Expecting a single outcome issue
            dumpOutcome(_generator.Outcome);
            Assert.IsNotNull(_generator.Outcome);
            var issues = _generator.Outcome?.Issue ?? new List<OperationOutcome.IssueComponent>();
            Assert.IsNotNull(issues);
            Assert.AreEqual(1, issues.Count);
            assertIssue(issues[0], SnapshotGenerator.PROFILE_ELEMENTDEF_INVALID_PROFILE_TYPE);

            // [WMR 20180115] NEW - Use alternative (iterative) approach for full expansion
            var elems = expanded.Snapshot.Element;
            issues = new List<OperationOutcome.IssueComponent>();
            elems = expanded.Snapshot.Element = await fullyExpand(elems, issues);
            Debug.WriteLine($"Fully expanded: {elems.Count} elements");

            expanded.Snapshot.Element.Dump();

            // Full expansion should also generate same outcome issue
            Assert.AreEqual(1, issues.Count);
            assertIssue(issues[0], SnapshotGenerator.PROFILE_ELEMENTDEF_INVALID_PROFILE_TYPE);
        }

        // [WMR 20170306] Verify that the snapshot generator determines and merges the correct base element for slices
        // * Slice entry is based on associated element in base profile with same path (and name)
        //   Slice entry inherits constraints from base element; can only further constrain
        //   Note: Base element may be a slice entry itself, or a named slice (in case of reslicing)
        // * Named slices are based on associated element in base profile with same path and parent slice name (same name as preceding slice entry)
        //   Same base element as preceding slice entry, but without the slicing component and with min = 0 (per definition for named slices, as they can be optional)

        //
        // Example:
        //
        // Patient (base profile)
        // - Patient.identifier
        //
        // MyPatient : Patient (user profile)
        // - Patient.identifier (slice entry)     => Patient.identifier (in Base)
        // - Patient.identifier:A                 => Patient.identifier (in Base)
        // - Patient.identifier:A/1               => Patient.identifier (in Base)
        // - Patient.identifier:A/2               => Patient.identifier (in Base)
        // - Patient.identifier:B                 => Patient.identifier (in Base)
        //
        // DerivedPatient : MyPatient (derived user profile)
        // - Patient.identifier (slice entry)     => Patient.identifier (slice entry) in MyPatient
        // - Patient.identifier:A                 => Patient.identifier:A in MyPatient
        // - Patient.identifier:A/1               => Patient.identifier:A/1 in MyPatient
        // - Patient.identifier:A/2               => Patient.identifier:A/2 in MyPatient
        // - Patient.identifier:A/3               => Patient.identifier:A in MyPatient
        // - Patient.identifier:B (reslice entry) => Patient.identifier:B in MyPatient
        // - Patient.identifier:B/1               => Patient.identifier:B in MyPatient
        // - Patient.identifier:B/2               => Patient.identifier:B in MyPatient
        // - Patient.identifier:C                 => Patient.identifier in MyPatient

        private static StructureDefinition SlicedPatientProfile => new()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
            Name = "MySlicedPatient",
            Url = "http://example.org/fhir/StructureDefinition/MySlicedPatient",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Patient.identifier")
                    {
                        Slicing = new ElementDefinition.SlicingComponent()
                        {
                            // Discriminator = new string[] { "system" },
                            Discriminator = new ElementDefinition.DiscriminatorComponent[]
                                { new ElementDefinition.DiscriminatorComponent
                                    { Type = ElementDefinition.DiscriminatorType.Value, Path = "system" }
                                }.ToList(),
                            Ordered = false,
                            Rules = ElementDefinition.SlicingRules.Open
                        },
                        Min = 1
                    }
                    ,new ElementDefinition("Patient.identifier")
                    {
                        SliceName = "bsn",
                        Min = 1,
                        Max = "1"
                    }
                    ,new ElementDefinition("Patient.identifier")
                    {
                        SliceName = "ehr_id",
                        Max = "2"
                    }
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestSliceBase_SlicedPatient()
        {
            var profile = SlicedPatientProfile;

            var resolver = new InMemoryResourceResolver(profile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);
            StructureDefinition expanded = null;

            _generator.PrepareElement += elementHandler;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(profile);
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }
            dumpOutcome(_generator.Outcome);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            var identifierConstraints = expanded.Snapshot.Element.Where(e => e.Path.StartsWith("Patient.identifier"));

            identifierConstraints.Dump("Constraints on Patient.identifier:");

            var corePatientProfile = await _testResolver.FindStructureDefinitionAsync(profile.BaseDefinition);
            Assert.IsNotNull(corePatientProfile);
            Assert.IsTrue(corePatientProfile.HasSnapshot);
            var corePatientIdentifierElem = corePatientProfile.Snapshot.Element.FirstOrDefault(e => e.Path == "Patient.identifier");
            Assert.IsNotNull(corePatientIdentifierElem);
            Debug.Print($"Base: #{corePatientIdentifierElem.GetHashCode()} '{corePatientIdentifierElem.Path}'");

            dumpBaseElems(identifierConstraints);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());

            // Verify slice entry
            Assert.IsTrue(nav.MoveToChild("identifier"));

            // [WMR 20170711] Disregard ElementDefinition.Base
            // Empty for elements introduced by core Patient profile, esp. corePatientIdentifierElem
            // Assert.AreEqual(corePatientIdentifierElem, GetBaseElementAnnotation(nav.Current));
            Assert.IsTrue(isAlmostExactly(corePatientIdentifierElem, GetBaseElementAnnotation(nav.Current)));

            Assert.IsNotNull(nav.Current.Slicing);
            Assert.IsNull(nav.Current.SliceName);
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("*", nav.Current.Max);

            // Verify slice "bsn"
            Assert.IsTrue(nav.MoveToNextSlice());

            // [WMR 20170711] Disregard ElementDefinition.Base
            // Assert.AreEqual(corePatientIdentifierElem, GetBaseElementAnnotation(nav.Current));
            Assert.IsTrue(isAlmostExactly(corePatientIdentifierElem, GetBaseElementAnnotation(nav.Current)));

            Assert.IsNull(nav.Current.Slicing);
            Assert.AreEqual("bsn", nav.Current.SliceName);
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("1", nav.Current.Max);

            // Verify slice "ehr_id"
            Assert.IsTrue(nav.MoveToNextSlice());

            // [WMR 20170711] Disregard ElementDefinition.Base
            // Assert.AreEqual(corePatientIdentifierElem, GetBaseElementAnnotation(nav.Current));
            Assert.IsTrue(isAlmostExactly(corePatientIdentifierElem, GetBaseElementAnnotation(nav.Current)));

            Assert.IsNull(nav.Current.Slicing);
            Assert.AreEqual("ehr_id", nav.Current.SliceName);
            Assert.AreEqual(0, nav.Current.Min);
            Assert.AreEqual("2", nav.Current.Max);
        }

        private static StructureDefinition NationalPatientProfile => new()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
            Name = "MyNationalPatient",
            Url = "http://example.org/fhir/StructureDefinition/MyNationalPatient",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Patient.identifier")
                    {
                        Comment = new Markdown("NationalPatientProfile")
                    },
                    new ElementDefinition("Patient.identifier.system")
                    {
                        Min = 1
                    }
                }
            }
        };

        private static StructureDefinition SlicedNationalPatientProfile => new()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = NationalPatientProfile.Url,
            Name = "SlicedNationalPatientProfile",
            Url = "http://example.org/fhir/StructureDefinition/SlicedNationalPatientProfile",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Patient.identifier")
                    {
                        Slicing = new ElementDefinition.SlicingComponent()
                        {
                            // Discriminator = new string[] { "system" },
                            Discriminator = new ElementDefinition.DiscriminatorComponent[]
                                { new ElementDefinition.DiscriminatorComponent
                                    { Type = ElementDefinition.DiscriminatorType.Value, Path = "system" }
                                }.ToList(),
                            Ordered = false,
                            Rules = ElementDefinition.SlicingRules.Open
                        },
                        Min = 1,
                        // Append to comment inherited from base
                        Comment = new Markdown("...SlicedNationalPatientProfile")
                    }
                    // Slice: bsn
                    ,new ElementDefinition("Patient.identifier")
                    {
                        SliceName = "bsn",
                        Min = 1,
                        Max = "1"
                    },
                    new ElementDefinition("Patient.identifier.system")
                    {
                        Fixed = new FhirUri("http://example.org/fhir/ValueSet/bsn")
                    },
                    // Slice: ehr_id
                    new ElementDefinition("Patient.identifier")
                    {
                        SliceName = "ehr_id",
                        Max = "2",
#if false
                        // Re-slice the ehr-id
                        Slicing = new ElementDefinition.SlicingComponent()
                        {
                            Discriminator = new string[] { "use" },
                            Ordered = true,
                            Rules = ElementDefinition.SlicingRules.Closed
                        }
#endif
                    },
#if false
                    // Reslice: ehr-id/temp
                    new ElementDefinition("Patient.identifier")
                    {
                        Name = "ehr_id/temp",
                        Max = "1",
                    },
                    new ElementDefinition("Patient.identifier.use")
                    {
                        // Fixed = new Code<Identifier.IdentifierUse>(Identifier.IdentifierUse.Temp)
                        Fixed = new Code("temp")
                    }
#endif
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestSliceBase_SlicedNationalPatient()
        {
            var baseProfile = NationalPatientProfile;
            var profile = SlicedNationalPatientProfile;

            var resolver = new InMemoryResourceResolver(baseProfile, profile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);
            StructureDefinition expanded = null;

            _generator.PrepareElement += elementHandler;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(profile);
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }
            dumpOutcome(_generator.Outcome);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            var identifierConstraints = expanded.Snapshot.Element.Where(e => e.Path.StartsWith("Patient.identifier"));

            identifierConstraints.Dump("Constraints on Patient.identifier:");

            var nationalPatientProfile = await resolver.FindStructureDefinitionAsync(profile.BaseDefinition);
            Assert.IsNotNull(nationalPatientProfile);
            Assert.IsTrue(nationalPatientProfile.HasSnapshot);
            var nationalPatientIdentifierElem = nationalPatientProfile.Snapshot.Element.FirstOrDefault(e => e.Path == "Patient.identifier");
            Assert.IsNotNull(nationalPatientIdentifierElem);
            Debug.Print($"Base: #{nationalPatientIdentifierElem.GetHashCode()} '{nationalPatientIdentifierElem.Path}'");

            dumpBaseElems(identifierConstraints);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());

            // Verify slice entry
            Assert.IsTrue(nav.MoveToChild("identifier"));

            // [WMR 20170711] Disregard ElementDefinition.Base
            // Assert.AreEqual(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current));
            Assert.IsTrue(isAlmostExactly(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current)));

            Assert.IsNotNull(nav.Current.Slicing);
            Assert.IsNull(nav.Current.SliceName);
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("*", nav.Current.Max);
            // Slice entry should inherit Comments from base element, merged with diff constraints
            // [WMR 20181212] R4 - Comment type changed from string to markdown
            Assert.AreEqual("NationalPatientProfile\r\nSlicedNationalPatientProfile", nav.Current.Comment);
            // Slice entry should also inherit constraints on child elements from base element
            var bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("system"));
            Assert.AreEqual(nav.Current.Min, 1);
            Assert.IsTrue(nav.ReturnToBookmark(bm));

            // Verify slice "bsn"
            Assert.IsTrue(nav.MoveToNextSlice());

            // [WMR 20170711] Disregard ElementDefinition.Base
            // Assert.AreEqual(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current));
            Assert.IsTrue(isAlmostExactly(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current)));

            Assert.IsNull(nav.Current.Slicing);
            Assert.AreEqual("bsn", nav.Current.SliceName);
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("1", nav.Current.Max);
            // Named slices should inherit Comments from base element
            // [WMR 20181212] R4 - Comment type changed from string to markdown
            Assert.AreEqual("NationalPatientProfile", nav.Current.Comment);
            // Named slices should also inherit constraints on child elements from base element
            bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("system"));
            Assert.AreEqual(nav.Current.Min, 1);
            // Should be merged with diff constraints on child elements
            Assert.AreEqual((nav.Current.Fixed as FhirUri).Value, "http://example.org/fhir/ValueSet/bsn");
            Assert.IsTrue(nav.ReturnToBookmark(bm));

            // Verify slice "ehr_id"
            Assert.IsTrue(nav.MoveToNextSlice());

            // [WMR 20170711] Disregard ElementDefinition.Base
            // Assert.AreEqual(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current));
            Assert.IsTrue(isAlmostExactly(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current)));

            Assert.IsNull(nav.Current.Slicing);
            Assert.AreEqual("ehr_id", nav.Current.SliceName);
            Assert.AreEqual(0, nav.Current.Min);
            Assert.AreEqual("2", nav.Current.Max);
            // Named slices should inherit Comments from base element
            // [WMR 20181212] R4 - Comment type changed from string to markdown
            Assert.AreEqual("NationalPatientProfile", nav.Current.Comment);
            // Named slices should also inherit constraints on child elements from base element
            bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("system"));
            Assert.AreEqual(nav.Current.Min, 1);
            Assert.IsTrue(nav.ReturnToBookmark(bm));

#if false
            // Verify re-slice "ehr_id/temp"
            Assert.IsTrue(nav.MoveToNextSliceAtAnyLevel());

            // [WMR 20170711] Disregard ElementDefinition.Base
            // Assert.AreEqual(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current));
            Assert.IsTrue(isAlmostExactly(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current)));

            Assert.IsNull(nav.Current.Slicing);
            Assert.AreEqual("ehr_id/temp", nav.Current.SliceName);
            Assert.AreEqual(0, nav.Current.Min);
            Assert.AreEqual("1", nav.Current.Max);
            // Named slices should inherit Comments from base element
            Assert.AreEqual("NationalPatientProfile", nav.Current.Comment);
            // Named slices should also inherit constraints on child elements from base element
            bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("system"));
            Assert.AreEqual(nav.Current.Min, 1);
            Assert.IsTrue(nav.ReturnToBookmark(bm));
#endif
        }

        private static StructureDefinition ReslicedNationalPatientProfile => new()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = NationalPatientProfile.Url,
            Name = "ReslicedNationalPatientProfile",
            Url = "http://example.org/fhir/StructureDefinition/ReslicedNationalPatientProfile",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Patient.identifier")
                    {
                        Slicing = new ElementDefinition.SlicingComponent()
                        {
                            Discriminator = new ElementDefinition.DiscriminatorComponent[]
                                { new ElementDefinition.DiscriminatorComponent
                                    { Type = ElementDefinition.DiscriminatorType.Value, Path = "system" }
                                }.ToList(),
                            Ordered = false,
                            Rules = ElementDefinition.SlicingRules.Open
                        },
                        Min = 1,
                        // Append to comment inherited from base
                        Comment = new Markdown("...ReslicedNationalPatientProfile")
                    }
                    // Slice: bsn
                    ,new ElementDefinition("Patient.identifier")
                    {
                        SliceName = "bsn",
                        Min = 1,
                        Max = "1"
                    },
                    new ElementDefinition("Patient.identifier.system")
                    {
                        Fixed = new FhirUri("http://example.org/fhir/ValueSet/bsn")
                    },
                    // Slice: ehr_id
                    new ElementDefinition("Patient.identifier")
                    {
                        SliceName = "ehr_id",
                        Max = "2",

                        // Re-slice the ehr-id
                        Slicing = new ElementDefinition.SlicingComponent()
                        {
                            // Discriminator = new string[] { "use" },
                            Discriminator = new ElementDefinition.DiscriminatorComponent[]
                                { new ElementDefinition.DiscriminatorComponent
                                    { Type = ElementDefinition.DiscriminatorType.Value, Path = "use" }
                                }.ToList(),
                            Ordered = true,
                            Rules = ElementDefinition.SlicingRules.Closed
                        }
                    },

                    // Reslice: ehr-id/temp
                    new ElementDefinition("Patient.identifier")
                    {
                        SliceName = "ehr_id/temp",
                        Max = "1",
                    },
                    new ElementDefinition("Patient.identifier.use")
                    {
                        // Fixed = new Code<Identifier.IdentifierUse>(Identifier.IdentifierUse.Temp)
                        Fixed = new Code("temp")
                    }
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestSliceBase_ReslicedNationalPatient()
        {
            var baseProfile = NationalPatientProfile;
            var profile = ReslicedNationalPatientProfile;
            var resolver = new InMemoryResourceResolver(baseProfile, profile);

            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);
            StructureDefinition expanded = null;

            _generator.PrepareElement += elementHandler;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(profile);
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }
            dumpOutcome(_generator.Outcome);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            var identifierConstraints = expanded.Snapshot.Element.Where(e => e.Path.StartsWith("Patient.identifier"));

            identifierConstraints.Dump("Constraints on Patient.identifier:");

            var nationalPatientProfile = await resolver.FindStructureDefinitionAsync(profile.BaseDefinition);
            Assert.IsNotNull(nationalPatientProfile);
            Assert.IsTrue(nationalPatientProfile.HasSnapshot);
            var nationalPatientIdentifierElem = nationalPatientProfile.Snapshot.Element.FirstOrDefault(e => e.Path == "Patient.identifier");
            Assert.IsNotNull(nationalPatientIdentifierElem);
            Debug.Print($"Base: #{nationalPatientIdentifierElem.GetHashCode()} '{nationalPatientIdentifierElem.Path}'");

            dumpBaseElems(identifierConstraints);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());

            // Verify slice entry
            Assert.IsTrue(nav.MoveToChild("identifier"));

            // [WMR 20170711] Disregard ElementDefinition.Base
            // Assert.AreEqual(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current));
            Assert.IsTrue(isAlmostExactly(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current)));

            Assert.IsNotNull(nav.Current.Slicing);
            Assert.IsNull(nav.Current.SliceName);
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("*", nav.Current.Max);
            // Slice entry should inherit Comments from base element, merged with diff constraints
            // [WMR 20181212] R4 - Comment type changed from string to markdown
            Assert.AreEqual("NationalPatientProfile\r\nReslicedNationalPatientProfile", nav.Current.Comment);
            // Slice entry should also inherit constraints on child elements from base element
            var bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("system"));
            Assert.AreEqual(nav.Current.Min, 1);
            Assert.IsTrue(nav.ReturnToBookmark(bm));

            // Verify slice "bsn"
            Assert.IsTrue(nav.MoveToNextSlice());

            // [WMR 20170711] Disregard ElementDefinition.Base
            // Assert.AreEqual(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current));
            Assert.IsTrue(isAlmostExactly(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current)));

            Assert.IsNull(nav.Current.Slicing);
            Assert.AreEqual("bsn", nav.Current.SliceName);
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("1", nav.Current.Max);
            // Named slices should inherit Comments from base element
            // [WMR 20181212] R4 - Comment type changed from string to markdown
            Assert.AreEqual("NationalPatientProfile", nav.Current.Comment);
            // Named slices should also inherit constraints on child elements from base element
            bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("system"));
            Assert.AreEqual(nav.Current.Min, 1);
            // Should be merged with diff constraints on child elements
            Assert.AreEqual((nav.Current.Fixed as FhirUri).Value, "http://example.org/fhir/ValueSet/bsn");
            Assert.IsTrue(nav.ReturnToBookmark(bm));

            // Verify slice "ehr_id"
            Assert.IsTrue(nav.MoveToNextSlice());

            // [WMR 20170711] Disregard ElementDefinition.Base
            // Assert.AreEqual(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current));
            Assert.IsTrue(isAlmostExactly(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current)));

            Assert.IsNotNull(nav.Current.Slicing);
            Assert.AreEqual("ehr_id", nav.Current.SliceName);
            Assert.AreEqual(0, nav.Current.Min);
            Assert.AreEqual("2", nav.Current.Max);
            // Named slices should inherit Comments from base element
            // [WMR 20181212] R4 - Comment type changed from string to markdown
            Assert.AreEqual("NationalPatientProfile", nav.Current.Comment);
            // Named slices should also inherit constraints on child elements from base element
            bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("system"));
            Assert.AreEqual(nav.Current.Min, 1);
            Assert.IsTrue(nav.ReturnToBookmark(bm));

            // Verify re-slice "ehr_id/temp"
            Assert.IsTrue(nav.MoveToFirstReslice());

            // [WMR 20170711] Disregard ElementDefinition.Base
            // Assert.AreEqual(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current));
            Assert.IsTrue(isAlmostExactly(nationalPatientIdentifierElem, GetBaseElementAnnotation(nav.Current)));

            Assert.IsNull(nav.Current.Slicing);
            Assert.AreEqual("ehr_id/temp", nav.Current.SliceName);
            Assert.AreEqual(0, nav.Current.Min);
            Assert.AreEqual("1", nav.Current.Max);
            // Named slices should inherit Comments from base element
            // [WMR 20181212] R4 - Comment type changed from string to markdown
            Assert.AreEqual("NationalPatientProfile", nav.Current.Comment);
            // Named slices should also inherit constraints on child elements from base element
            bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("system"));
            Assert.AreEqual(nav.Current.Min, 1);
            Assert.IsTrue(nav.ReturnToBookmark(bm));
        }

        [TestMethod]
        public async Tasks.Task TestSliceBase_PatientTelecomResliceEK()
        {
            var dirSource = new DirectorySource("TestData/validation");
            var source = new TimingSource(dirSource);
            var resolver = new CachedResolver(source);
            var multiResolver = new MultiResolver(resolver, _testResolver);

            var profile = await resolver.FindStructureDefinitionAsync("http://example.com/StructureDefinition/patient-telecom-reslice-ek");
            Assert.IsNotNull(profile);

            var settings = new SnapshotGeneratorSettings(_settings)
            {
                GenerateElementIds = true
            };
            _generator = new SnapshotGenerator(multiResolver, settings);
            StructureDefinition expanded = null;

            _generator.PrepareElement += elementHandler;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(profile);
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }
            dumpOutcome(_generator.Outcome);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            expanded.Snapshot.Element.Dump();

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());

            // Patient.telecom slice entry
            Assert.IsTrue(nav.MoveToChild("telecom"));
            Assert.IsNotNull(nav.Current.Slicing);
            Assert.AreEqual(true, nav.Current.Slicing.Ordered);
            Assert.AreEqual(ElementDefinition.SlicingRules.OpenAtEnd, nav.Current.Slicing.Rules);
            Assert.IsFalse(nav.Current.Slicing.Discriminator.Any());
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("5", nav.Current.Max);

            // Patient.telecom:phone
            Assert.IsTrue(nav.MoveToNext("telecom"));
            Assert.AreEqual("phone", nav.Current.SliceName);
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("2", nav.Current.Max);
            Assert.IsNull(nav.Current.Slicing);

            // Patient.telecom.system
            var bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("system"));
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("phone", (nav.Current.Fixed as Code)?.Value);
            Assert.IsTrue(nav.ReturnToBookmark(bm));

            // Patient.telecom:email
            Assert.IsTrue(nav.MoveToNext("telecom"));
            Assert.AreEqual("email", nav.Current.SliceName);
            Assert.AreEqual(0, nav.Current.Min);
            Assert.AreEqual("1", nav.Current.Max);
            Assert.IsNotNull(nav.Current.Slicing);
            // TODO: BRIAN: Need to check that this is the correct assertion here
            Assert.AreEqual("system|use", string.Join("|", nav.Current.Slicing.Discriminator.Select(s => s.Path)));
            // Assert.AreEqual(1, nav.Current.Slicing.Discriminator.SelectMany(s => s.Type.Value).Count()));
            Assert.AreEqual(ElementDefinition.SlicingRules.Closed, nav.Current.Slicing.Rules);
            // Assert.AreEqual(false, nav.Current.Slicing.Ordered);
            Assert.IsNull(nav.Current.Slicing.Ordered);

            // Patient.telecom.system
            bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("system"));
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("email", (nav.Current.Fixed as Code)?.Value);
            Assert.IsTrue(nav.ReturnToBookmark(bm));

            // Patient.telecom:email/home
            Assert.IsTrue(nav.MoveToNext("telecom"));
            Assert.AreEqual("email/home", nav.Current.SliceName);
            Assert.AreEqual(0, nav.Current.Min);
            Assert.AreEqual("1", nav.Current.Max);
            Assert.IsNull(nav.Current.Slicing);

            // Patient.telecom.system
            bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("system"));
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("email", (nav.Current.Fixed as Code)?.Value);
            Assert.IsTrue(nav.MoveToNext("use"));
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("home", (nav.Current.Fixed as Code)?.Value);
            Assert.IsTrue(nav.ReturnToBookmark(bm));

            // Patient.telecom:email/work
            Assert.IsTrue(nav.MoveToNext("telecom"));
            Assert.AreEqual("email/work", nav.Current.SliceName);
            Assert.AreEqual(0, nav.Current.Min);
            Assert.AreEqual("1", nav.Current.Max);
            Assert.IsNull(nav.Current.Slicing);

            // Patient.telecom.system
            bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("system"));
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("email", (nav.Current.Fixed as Code)?.Value);
            Assert.IsTrue(nav.MoveToNext("use"));
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("work", (nav.Current.Fixed as Code)?.Value);
            Assert.IsTrue(nav.ReturnToBookmark(bm));

            // Patient.telecom:other
            Assert.IsTrue(nav.MoveToNext("telecom"));
            Assert.AreEqual("other", nav.Current.SliceName);
            Assert.AreEqual(0, nav.Current.Min);
            Assert.AreEqual("3", nav.Current.Max);
            Assert.IsNotNull(nav.Current.Slicing);
            Assert.AreEqual("system|use", string.Join("|", nav.Current.Slicing.Discriminator.Select(p => p.Path)));
            Assert.AreEqual(ElementDefinition.SlicingRules.Open, nav.Current.Slicing.Rules);
            // Assert.AreEqual(false, nav.Current.Slicing.Ordered);
            Assert.IsNull(nav.Current.Slicing.Ordered);

            // Patient.telecom.system
            bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("system"));
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("other", (nav.Current.Fixed as Code)?.Value);
            Assert.IsTrue(nav.ReturnToBookmark(bm));

            // Patient.telecom:other/home
            Assert.IsTrue(nav.MoveToNext("telecom"));
            Assert.AreEqual("other/home", nav.Current.SliceName);
            Assert.AreEqual(0, nav.Current.Min);
            Assert.AreEqual("1", nav.Current.Max);
            Assert.IsNull(nav.Current.Slicing);

            // Patient.telecom.system
            bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("system"));
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("other", (nav.Current.Fixed as Code)?.Value);
            Assert.IsTrue(nav.MoveToNext("use"));
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("home", (nav.Current.Fixed as Code)?.Value);
            Assert.IsTrue(nav.ReturnToBookmark(bm));

            // Patient.telecom:other/work
            Assert.IsTrue(nav.MoveToNext("telecom"));
            Assert.AreEqual("other/work", nav.Current.SliceName);
            Assert.AreEqual(0, nav.Current.Min);
            Assert.AreEqual("1", nav.Current.Max);
            Assert.IsNull(nav.Current.Slicing);

            // Patient.telecom.system
            bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("system"));
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("other", (nav.Current.Fixed as Code)?.Value);
            Assert.IsTrue(nav.MoveToNext("use"));
            Assert.AreEqual(1, nav.Current.Min);
            Assert.AreEqual("work", (nav.Current.Fixed as Code)?.Value);
            Assert.IsTrue(nav.ReturnToBookmark(bm));
        }

        [TestMethod]
        public async Tasks.Task TestElementMappings()
        {
            var profile = await _testResolver.FindStructureDefinitionAsync("http://hl7.org/fhir/StructureDefinition/Patient");
            Assert.IsNotNull(profile);

            var diffElem = profile.Differential.Element.FirstOrDefault(e => e.Path == "Patient.identifier");
            Assert.IsNotNull(diffElem);
            dumpMappings(diffElem);

            profile.Snapshot = null; // remove snapshot, so it can generated again below

            StructureDefinition expanded = null;
            _generator = new SnapshotGenerator(_testResolver, _settings);
            _generator.PrepareElement += elementHandler;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(profile);
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }
            dumpOutcome(_generator.Outcome);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            var elems = expanded.Snapshot.Element;
            elems.Dump();

            var elem = elems.FirstOrDefault(e => e.Path == "Patient.identifier");
            Assert.IsNotNull(elem);
            dumpMappings(elem);

            // Snapshot element mappings should include all of the differential element mappings
            Assert.IsTrue(diffElem.Mapping.All(dm => elem.Mapping.Any(m => m.IsExactly(dm))));

        }

        private static void dumpMappings(ElementDefinition elem) => dumpMappings(elem.Mapping, $"Mappings for {elem.Path}:");

        private static void dumpMappings(IList<ElementDefinition.MappingComponent> mappings, string header = null)
        {
            Debug.WriteLineIf(header != null, header);
            foreach (var mapping in mappings)
            {
                Debug.Print($"{mapping.Identity} : {mapping.Map}");
            }
        }

        // Ewout: type slices cannot contain renamed elements!

        private static StructureDefinition PatientNonTypeSliceProfile => new()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
            Name = "NonTypeSlicePatient",
            Url = "http://example.org/fhir/StructureDefinition/NonTypeSlicePatient",
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Patient.deceased[x]")
                    {
                        Min = 1,
                        // Repeat the base element types (no additional constraints)
                        Type = new List<ElementDefinition.TypeRefComponent>()
                        {
                            new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.Boolean.GetLiteral() },
                            new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.DateTime.GetLiteral() }
                        }
                    }
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestPatientNonTypeSlice()
        {
            var profile = PatientNonTypeSliceProfile;

            var resolver = new InMemoryResourceResolver(profile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            // Force expansion of Patient.deceased[x]
            var nav = ElementDefinitionNavigator.ForDifferential(profile);
            Assert.IsTrue(nav.MoveToFirstChild());
            var result = await _generator.ExpandElementAsync(nav);
            profile.Differential.Element.Dump();
            dumpOutcome(_generator.Outcome);
            Assert.IsTrue(result);

            Assert.IsNull(_generator.Outcome);
        }

        // Ewout: type slices cannot contain renamed elements!
        private static StructureDefinition ObservationSimpleQuantityProfile => new()
        {
            Type = FHIRAllTypes.Observation.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation),
            Name = "NonTypeSlicePatient",
            Url = "http://example.org/fhir/StructureDefinition/ObservationSimpleQuantityProfile",
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Observation.valueQuantity")
                    {
                        // Repeat the base element types (no additional constraints)
                        Type = new List<ElementDefinition.TypeRefComponent>()
                        {
                            new ElementDefinition.TypeRefComponent()
                            {
                                // Constrain Quantity to SimpleQuantity
                                Code = FHIRAllTypes.Quantity.GetLiteral(),
                                Profile = new string[] { ModelInfo.CanonicalUriForFhirCoreType("SimpleQuantity") }
                            },
                        }
                    }
                }
            }
        };

        // [WMR 20170321] NEW
        [TestMethod]
        public async Tasks.Task TestSimpleQuantityObservationProfile()
        {
            var profile = ObservationSimpleQuantityProfile;

            var resolver = new InMemoryResourceResolver(profile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(profile);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);
            expanded.Snapshot.Element.Where(e => e.Path.StartsWith("Observation.value")).Dump();
            dumpOutcome(_generator.Outcome);

            // [WMR 20181212] R4 FIXED - SimpleQuantity core type definition has been fixed
            //var issues = _generator.Outcome?.Issue;
            //Assert.AreEqual(1, issues.Count);
            //assertIssue(issues[0], SnapshotGenerator.PROFILE_ELEMENTDEF_INVALID_SLICENAME_ON_ROOT);
            Assert.IsNull(_generator.Outcome);

            // [WMR 20180115] NEW - Use alternative (iterative) approach for full expansion
            var issues = new List<OperationOutcome.IssueComponent>();
            var elems = expanded.Snapshot.Element;
            elems = expanded.Snapshot.Element = await fullyExpand(elems, issues);
            // Generator should report same issue as during regular snapshot expansion
            //Assert.AreEqual(1, issues.Count);
            //assertIssue(issues[0], SnapshotGenerator.PROFILE_ELEMENTDEF_INVALID_SLICENAME_ON_ROOT);
            Assert.AreEqual(0, issues.Count);

            // Ensure that renamed diff elements override base elements with original names
            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);

            // [WMR 20190211] FIXED
            // STU3: Snapshot should not contain elements with original name
            // Assert.IsFalse(nav.JumpToFirst("Observation.value[x]"));
            // R4: Snapshot may contain both "[x]" and also renamed element constraints
            Assert.IsTrue(nav.JumpToFirst("Observation.value[x]"));

            // Snapshot should contain renamed elements
#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.JumpToFirst("Observation.value[x]"));
            Assert.IsTrue(nav.MoveToNextSlice("valueQuantity"));
#else
            Assert.IsTrue(nav.JumpToFirst("Observation.valueQuantity"));
#endif

            Assert.IsNotNull(nav.Current.Type);
            Assert.AreEqual(1, nav.Current.Type.Count);
            Assert.AreEqual(FHIRAllTypes.Quantity.GetLiteral(), nav.Current.Type[0].Code);

            var type = nav.Current.Type.First();
            Debug.Print($"{nav.Path} : {type.Code} - '{type.Profile}'");
        }

        //Ignore invalid slice name error on the root of SimpleQuantity.
        [TestMethod]
        public async Tasks.Task TestSimpleQuantity()
        {
            var resource = await _testResolver.FindStructureDefinitionAsync(ModelInfo.CanonicalUriForFhirCoreType("SimpleQuantity"));
            _generator = new SnapshotGenerator(_testResolver);
            var snapshot = await _generator.GenerateAsync(resource);
            Assert.IsNotNull(snapshot);
            Assert.IsNull(snapshot.GetRootElement().SliceName);
            Assert.IsNull(_generator.Outcome);
        }

        // [WMR 20170406] NEW
        // Issue reported by Vadim
        // Complex extension:   structure.cdstools-typedstage
        // Referencing Profile: structure.cdstools-basecancer
        // Profile defines constraints on child elements of the complex extension
        // Snapshot generator adds slicing component to Condition.extension.extension.extension:type - WRONG!
        [TestMethod]   // test data needs to be converted from dstu2 -> stu3
        public async Tasks.Task TestProfileConstraintsOnComplexExtensionChildren()
        {
            var profile = await _testResolver.FindStructureDefinitionAsync("https://example.org/fhir/StructureDefinition/cds-basecancer");
            Assert.IsNotNull(profile);

            profile.Differential.Element.Dump("===== Differential =====");

            StructureDefinition expanded = null;
            _generator = new SnapshotGenerator(_testResolver, _settings);
            _generator.PrepareElement += elementHandler;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(profile);
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }
            dumpOutcome(_generator.Outcome);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            var elems = expanded.Snapshot.Element;
            elems.Dump("===== Snapshot =====");

            var nav = new ElementDefinitionNavigator(elems);
            Assert.IsTrue(nav.MoveToFirstChild());
            Assert.AreEqual("Condition", nav.Path);

            // Condition.extension (slicing entry)
            Assert.IsTrue(nav.MoveToChild("extension"));
            Assert.IsNotNull(nav.Current.Slicing);
            Assert.AreEqual("url", nav.Current.Slicing.Discriminator?.FirstOrDefault()?.Path);
            Assert.IsNull(nav.Current.SliceName);

            // Condition.extension:typedStaging
            Assert.IsTrue(nav.MoveToNext());
            Assert.AreEqual("typedStaging", nav.Current.SliceName);
            Assert.IsNull(nav.Current.Slicing);

            // Condition.extension:typedStaging.extension (slicing entry)
            Assert.IsTrue(nav.MoveToChild("extension"));
            Assert.IsNotNull(nav.Current.Slicing);
            Assert.AreEqual("url", nav.Current.Slicing.Discriminator?.FirstOrDefault()?.Path);
            Assert.IsNull(nav.Current.SliceName);

            // Condition.extension:typedStaging.extension:summary
            Assert.IsTrue(nav.MoveToNext());
            Assert.AreEqual("summary", nav.Current.SliceName);
            Assert.IsNull(nav.Current.Slicing);

            // Condition.extension:typedStaging.extension:assessment
            Assert.IsTrue(nav.MoveToNext());
            Assert.AreEqual("assessment", nav.Current.SliceName);
            Assert.IsNull(nav.Current.Slicing);

            // Condition.extension:typedStaging.extension:type
            Assert.IsTrue(nav.MoveToNext());
            Assert.AreEqual("type", nav.Current.SliceName);
            Assert.IsNull(nav.Current.Slicing); // BUG!

            // Condition.extension:typedStaging.extension:type.valueCodeableConcept
#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.MoveToChild("value[x]"));
            Assert.IsTrue(nav.MoveToNextSlice("valueCodeableConcept"));
#else
            Assert.IsTrue(nav.MoveToChild("valueCodeableConcept"));
#endif
            Assert.IsNotNull(nav.Current.Binding);
            var valueSetReference = nav.Current.Binding.ValueSet;
            Assert.IsNotNull(valueSetReference);
            Assert.AreEqual(BindingStrength.Required, nav.Current.Binding.Strength);
            Assert.AreEqual("https://example.org/fhir/ValueSet/cds-cancerstagingtype", valueSetReference);
        }

        // [WMR 20170424] For debugging ElementIdGenerator

        private static StructureDefinition TestQuestionnaireProfile => new()
        {
            Type = FHIRAllTypes.Questionnaire.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Questionnaire),
            Name = "TestQuestionnaire",
            Url = "http://example.org/fhir/StructureDefinition/MyTestQuestionnaire",
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Questionnaire.url")
                    {
#if CUSTOM_ELEMENT_IDS
                        // Override default element id
                        // [WMR 20190822] R4: No longer allowed/supported
                        // http://hl7.org/fhir/elementdefinition.html#id
                        // SnapGen now always emits standardized element ids
                        ElementId = "CustomId"
#endif
                    },
                    // Verify that slices receive unique element id
                    new ElementDefinition("Questionnaire.code")
                    {
                        Slicing = new ElementDefinition.SlicingComponent()
                        {
                            Discriminator = new List<ElementDefinition.DiscriminatorComponent>()
                            {
                                new ElementDefinition.DiscriminatorComponent()
                                {
                                    Type = ElementDefinition.DiscriminatorType.Value,
                                    Path = "system"
                                }
                            }
                        }
                    },
                    new ElementDefinition("Questionnaire.code")
                    {
                        SliceName = "CodeA"
                    },
                    new ElementDefinition("Questionnaire.code")
                    {
                        SliceName = "CodeB"
                    },
                    // cf. BasicValidationTests.ValidateOverNameRef
                    new ElementDefinition("Questionnaire.item.item.type")
                    {
                        Fixed = new Code("decimal")
                    }
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestElementIds_Questionnaire()
        {
#if false // DEBUG
            var coreProfile = await _testResolver.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.Questionnaire);
            Assert.IsNotNull(coreProfile);
            Debug.WriteLine("Core Questionnaire:");
            foreach (var elem in coreProfile.Differential.Element)
            {
                Debug.WriteLine($"{elem.Path} | {elem.SliceName} | Id = {elem.ElementId} | Ref = {elem.ContentReference}");
            }

            _generator = new SnapshotGenerator(_testResolver, _settings);
            await _generator.UpdateAsync(coreProfile);
            dumpOutcome(_generator.Outcome);
#endif

            var profile = TestQuestionnaireProfile;
            var resolver = new InMemoryResourceResolver(profile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            var urlElement = profile.Differential.Element[0];

            _generator.PrepareElement += elementHandler;
            StructureDefinition expanded = null;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(profile);

                Assert.IsNotNull(expanded);
                Assert.IsTrue(expanded.HasSnapshot);
                dumpOutcome(_generator.Outcome);
                Assert.IsNull(_generator.Outcome);

                var elems = expanded.Snapshot.Element;
                Debug.WriteLine($"Default snapshot: #{elems.Count} elements");
                dumpBaseElems(elems);

#if CUSTOM_ELEMENT_IDS
                // Verify overriden element id in default snapshot
                // [WMR 20190822] R4: No longer allowed/supported
                // http://hl7.org/fhir/elementdefinition.html#id
                // SnapGen now always emits standardized element ids
                var elem = elems.FirstOrDefault(e => e.Path == urlElement.Path);
                Assert.IsNotNull(elem);
                Assert.AreEqual(urlElement.ElementId, elem.ElementId);
#endif

                // [WMR 20180115] NEW - Use alternative (iterative) approach for full expansion
                // IMPORTANT: also hook elementHandler event during fullExpansion, to emit (custom) base element annotations
                var issues = new List<OperationOutcome.IssueComponent>();
                elems = expanded.Snapshot.Element = await fullyExpand(elems, issues);
                Assert.AreEqual(0, issues.Count);
                Debug.WriteLine($"Full expansion: #{elems.Count} elements");
                dumpBaseElems(elems);

#if CUSTOM_ELEMENT_IDS
                // ExpandElement should NOT re-generate the id of the specified element; only for newly expanded children!
                // Verify overriden element id in full expansion
                var elem = elems.FirstOrDefault(e => e.Path == urlElement.Path);
                Assert.IsNotNull(elem);
                Assert.AreEqual(urlElement.ElementId, elem.ElementId);
#endif
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }

            Debug.WriteLine("Derived Questionnaire:");
            foreach (var elem in expanded.Snapshot.Element)
            {
                var baseElem = elem.Annotation<BaseDefAnnotation>()?.BaseElementDefinition;
                Debug.WriteLine($"{elem.Path} | {elem.SliceName} | Id = {elem.ElementId} | Base Id = {baseElem?.ElementId}");
                Assert.IsNotNull(elem.ElementId);
                Assert.IsNotNull(baseElem);
                Assert.IsNotNull(baseElem.ElementId);

                if (elem.Path != urlElement.Path)
                {
                    var equalLength = !elem.Path.StartsWith("Questionnaire.item.item.");
                    assertElementIds(elem, baseElem, equalLength);
                }
            }
        }

        private static void assertElementIds(ElementDefinition elem, ElementDefinition baseElem, bool equalLength = true)
        {
            // [WMR 20170614] derived profile may (further) slice the base profile
            // Element id's are not exactly equal, as the diff id's will introduce slice name(s)
            // => Strip slice names from id; path segments should be equal
            var idSegments = ElementIdGenerator.ParseId(elem.ElementId);
            var baseIdSegments = ElementIdGenerator.ParseId(baseElem.ElementId);

            // Determine if the base element has the same root (i.e. represents base profile of the same type)
            // If so, then the element ids should have the same number of segments
            if (equalLength && idSegments.FirstOrDefault() == baseIdSegments.FirstOrDefault())
            {
                Assert.AreEqual(baseIdSegments.Length, idSegments.Length);
            }

            // [WMR 20170710] Leading path segment(s) can differ, e.g. Patient.identifier.id <=> Identifier.id
            var idSegment = ElementIdSegment.Empty;
            var offset = idSegments.Length - baseIdSegments.Length;
            for (int i = 1; i < baseIdSegments.Length; i++)
            {
                idSegment = ElementIdSegment.Parse(idSegments[offset + i]);

                // Verify that the element name matches the base element name
                // Note: element ids of type slices should use original element name ending with "[x]"
                var baseIdSegment = ElementIdSegment.Parse(baseIdSegments[i]);
                Assert.AreEqual(baseIdSegment.ElementName, idSegment.ElementName);

                // If the base element id introduces a slice name, then derived element id should also include it
                // However derived profiles can introduce additional slices
                Assert.IsTrue(baseIdSegment.ElementName == null || idSegment.ElementName == baseIdSegment.ElementName);
            }

            // Verify the last element id segment = "elementName[:sliceName]"
            var basePath = elem.Base?.Path;
            var elemPath = basePath != null && ElementDefinitionNavigator.IsChoiceTypeElement(basePath) ? basePath : elem.Path;

            if (baseIdSegments.Length == 1)
            {
                // [WMR 20170710] initialize idSegment to the last segment
                idSegment = ElementIdSegment.Parse(idSegments[idSegments.Length - 1]);
            }

            Assert.AreEqual(ProfileNavigationExtensions.GetNameFromPath(elemPath), idSegment.ElementName);
            Assert.AreEqual(elem.SliceName, idSegment.SliceName);

        }

        private static StructureDefinition TestPatientTypeSliceProfile => new()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
            Name = "TestPatientWithTypeSlice",
            Url = "http://example.org/fhir/StructureDefinition/TestPatientWithTypeSlice",
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Patient.deceasedDateTime")
                    {
                        SliceName = "deceasedDateTime",
                        Type = new List<ElementDefinition.TypeRefComponent>()
                        {
                            new ElementDefinition.TypeRefComponent()
                            {
                                Code = FHIRAllTypes.DateTime.GetLiteral()
                            }
                        }
                    },
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestElementIds_PatientWithTypeSlice()
        {
            var profile = TestPatientTypeSliceProfile;
            var resolver = new InMemoryResourceResolver(profile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            _generator.PrepareElement += elementHandler;
            try
            {
                (_, var expanded) = await generateSnapshotAndCompare(profile);
                Assert.IsNotNull(expanded);
                Assert.IsTrue(expanded.HasSnapshot);
                dumpOutcome(_generator.Outcome);
                Assert.IsNull(_generator.Outcome);

                // [WMR 20180115] NEW - Use alternative (iterative) approach for full expansion
                var issues = _generator.Outcome?.Issue ?? new List<OperationOutcome.IssueComponent>();
                expanded.Snapshot.Element = await fullyExpand(expanded.Snapshot.Element, issues);
                dumpIssues(issues);
                Assert.AreEqual(0, issues.Count);

                Debug.WriteLine("Patient with type slice:");
                foreach (var elem in expanded.Snapshot.Element)
                {
                    var baseElem = elem.Annotation<BaseDefAnnotation>()?.BaseElementDefinition;
                    Debug.WriteLine($"{elem.Path} | {elem.SliceName} | Id = {elem.ElementId} | Base Id = {baseElem?.ElementId}");
                    Assert.IsNotNull(elem.ElementId);
                    Assert.IsNotNull(baseElem);
                    Assert.IsNotNull(baseElem.ElementId);

                    assertElementIds(elem, baseElem);
                }
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }
        }

        // [WMR 20170616] NEW - Test custom element IDs

        private static StructureDefinition TestSlicedPatientWithCustomIdProfile => new()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
            Name = "TestSlicedPatientWithCustomIdProfile",
            Url = "http://example.org/fhir/StructureDefinition/TestSlicedPatientWithCustomIdProfile",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Patient.identifier")
                    {
                        ElementId = "Patient.identifier",
                        Slicing = new ElementDefinition.SlicingComponent()
                        {
                            Discriminator = new List<ElementDefinition.DiscriminatorComponent>()
                            {
                                new ElementDefinition.DiscriminatorComponent()
                                {
                                    Type = ElementDefinition.DiscriminatorType.Value,
                                    Path = "system"
                                }
                            }
                        }
                    },
                    new ElementDefinition("Patient.identifier")
                    {
                        // Slice with custom ElementID
                        ElementId = "CUSTOM",
                        SliceName = "bsn"
                    },
                    new ElementDefinition("Patient.identifier.use")
                    {
                        // Should receive ElementID = "Patient.identifier:bsn.use"
                        Min = 1
                    }
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestElementIds_SlicedPatientWithCustomIdProfile()
        {
            var profile = TestSlicedPatientWithCustomIdProfile;
            var resolver = new InMemoryResourceResolver(profile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            const string sliceName = "bsn";
            var slice = profile.Differential.Element.FirstOrDefault(e => e.SliceName == sliceName);
            Assert.IsNotNull(slice);

            _generator.PrepareElement += elementHandler;
            StructureDefinition expanded = null;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(profile);
                Assert.IsNotNull(expanded);
                Assert.IsTrue(expanded.HasSnapshot);
                dumpOutcome(_generator.Outcome);

                // [WMR 20180115] NEW - Use alternative (iterative) approach for full expansion
                var issues = _generator.Outcome?.Issue ?? new List<OperationOutcome.IssueComponent>();
                expanded.Snapshot.Element = await fullyExpand(expanded.Snapshot.Element, issues);
                dumpIssues(issues);
                Assert.AreEqual(0, issues.Count);
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }

            var elems = expanded.Snapshot.Element;

            Debug.WriteLine("Sliced Patient with custom element id on slice:");
            foreach (var elem in elems)
            {
                var baseElem = elem.Annotation<BaseDefAnnotation>()?.BaseElementDefinition;
                Debug.WriteLine($"{elem.Path} | {elem.SliceName} | Id = {elem.ElementId} | Base Id = {baseElem?.ElementId}");
                Assert.IsNotNull(elem.ElementId);
                Assert.IsNotNull(baseElem);
                Assert.IsNotNull(baseElem.ElementId);

                if (elem.ElementId?.StartsWith("CUSTOM") == true)
                {
                    Assert.AreEqual(elem.SliceName, sliceName);
                }
                else
                {
                    assertElementIds(elem, baseElem);
                }
            }

            // [WMR 20170711] Additional assertions on children of named slice
            var slicePos = elems.FindIndex(e => e.SliceName == "bsn");
            Assert.AreNotEqual(-1, slicePos);
            var elemDef = elems[slicePos];
            Assert.AreEqual("Patient.identifier", elemDef.Path);
            // Verify that the id of all child elements includes parent slice name, i.e. starts with "Patient.identifier:bsn"
            for (var idx = slicePos + 1; idx < elems.Count; idx++)
            {
                elemDef = elems[idx];
                if (!ElementDefinitionNavigator.IsChildPath("Patient.identifier", elemDef.Path)) { break; }
                Assert.IsTrue(elemDef.ElementId.StartsWith("Patient.identifier:bsn"), $"Invalid element id at element #{idx}: {elemDef.ElementId}");
            }

            // [WMR 20170711] Dynamically update the slice name and re-generate ids for the subtree
            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.JumpToFirst(slice.Path));
            Assert.IsTrue(nav.MoveToNextSliceAtAnyLevel(sliceName));
            slice = nav.Current;
            Assert.AreEqual(slice.SliceName, sliceName);
            slice.SliceName = "CHANGED";
            ElementIdGenerator.Update(nav, true);

            // Verify that the id of all child elements includes updated slice name, i.e. starts with "Patient.identifier:CHANGED"
            for (var idx = slicePos + 1; idx < elems.Count; idx++)
            {
                elemDef = elems[idx];
                if (!ElementDefinitionNavigator.IsChildPath("Patient.identifier", elemDef.Path)) { break; }
                Assert.IsTrue(elemDef.ElementId.StartsWith("Patient.identifier:CHANGED"), $"Invalid element id at element #{idx}: {elemDef.ElementId}");
            }

        }

        [TestMethod]
        public async Tasks.Task TestElementIds_SlicedPatientWithCustomIdProfile2()
        {
            var profile = await _testResolver.FindStructureDefinitionAsync("http://example.org/fhir/StructureDefinition/PatientWithCustomElementIds");
            Assert.IsNotNull(profile);

            _generator = new SnapshotGenerator(_testResolver, _settings);
            _generator.PrepareElement += elementHandler;
            StructureDefinition expanded = null;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(profile);
                Assert.IsNotNull(expanded);
                Assert.IsTrue(expanded.HasSnapshot);
                dumpOutcome(_generator.Outcome);

                // [WMR 20180115] NEW - Use alternative (iterative) approach for full expansion
                var issues = _generator.Outcome?.Issue ?? new List<OperationOutcome.IssueComponent>();
                expanded.Snapshot.Element = await fullyExpand(expanded.Snapshot.Element, issues);
                dumpIssues(issues);
                Assert.AreEqual(0, issues.Count);
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }

            Debug.WriteLine("Sliced Patient with custom element id on slice:");
            foreach (var elem in expanded.Snapshot.Element)
            {
                var baseElem = elem.Annotation<BaseDefAnnotation>()?.BaseElementDefinition;
                Debug.WriteLine($"{elem.Path} | {elem.SliceName} | Id = {elem.ElementId} | Base Id = {baseElem?.ElementId}");
                Assert.IsNotNull(elem.ElementId);
                Assert.IsNotNull(baseElem);
                Assert.IsNotNull(baseElem.ElementId);

                if (elem.ElementId?.StartsWith("CUSTOM-") != true)
                {
                    assertElementIds(elem, baseElem);
                }
            }
        }


        // [WMR 20170426] NEW - Bug with generating base element annotations for merged external type profiles?
        [TestMethod]
        public async Tasks.Task TestPatientWithAddress()
        {
            var sd = await _testResolver.FindStructureDefinitionAsync(@"http://example.org/fhir/StructureDefinition/MyPatientWithAddress");
            Assert.IsNotNull(sd);

            _generator = new SnapshotGenerator(_testResolver, _settings);
            _generator.PrepareElement += elementHandler;
            try
            {
                await _generator.UpdateAsync(sd);
                dumpOutcome(_generator.Outcome);

                // [WMR 20180115] NEW - Use alternative (iterative) approach for full expansion
                var issues = _generator.Outcome?.Issue ?? new List<OperationOutcome.IssueComponent>();
                sd.Snapshot.Element = await fullyExpand(sd.Snapshot.Element, issues);
                dumpIssues(issues);
                Assert.AreEqual(0, issues.Count);
            }
            finally
            {
                _generator.PrepareElement -= elementHandler;
            }

            dumpBaseDefId(sd);

            var sdCore = await _testResolver.FindStructureDefinitionForCoreTypeAsync(sd.Type);
            dumpBaseDefId(sdCore);

            // Verify that main profile MyPatientWithAddress inherited
            // constraints from extension profile MyPatientExtension
            var elem = sd.Snapshot.Element.FirstOrDefault(e => e.SliceName == "patientExtension");
            Assert.IsNotNull(elem);
            Assert.AreEqual(@"http://example.org/fhir/StructureDefinition/MyPatientExtension", elem.Type[0]?.Profile.FirstOrDefault());
            var sdExt = await _testResolver.FindExtensionDefinitionAsync(elem.Type[0].Profile.FirstOrDefault());
            Assert.IsNotNull(sdExt);
            var extRootshort = sdExt.Differential.Element[0].Short; // Explicit constraint on ext root
            Assert.IsNotNull(extRootshort);
            Assert.IsTrue(sdExt.HasSnapshot);
            Assert.AreEqual(extRootshort, sdExt.Snapshot.Element[0].Short); // Verify propagation to snapshot
            Assert.AreEqual(extRootshort, elem.Short);  // Verify inherited by referencing profile
            var baseElem = elem.Annotation<BaseDefAnnotation>()?.BaseElementDefinition;
            Assert.IsNotNull(baseElem);
            Assert.AreEqual(extRootshort, baseElem.Short);

            // Verify that main profile MyPatientWithAddress inherited
            // constraints from element type profile MyPatientAddress
            elem = sd.Snapshot.Element.FirstOrDefault(e => e.Path == "Patient.address");
            Assert.IsNotNull(elem);

            Assert.AreEqual(@"http://example.org/fhir/StructureDefinition/MyPatientAddress", elem.Type[0]?.Profile.FirstOrDefault());
            var sdType = await _testResolver.FindStructureDefinitionAsync(elem.Type[0].Profile.FirstOrDefault());
            Assert.IsNotNull(sdType);
            var typeChildElem = sdType.Snapshot.Element.FirstOrDefault(e => e.Path == "Address.country");
            Assert.IsNotNull(typeChildElem);
            Assert.AreEqual("land", typeChildElem.Alias.FirstOrDefault());

            elem = sd.Snapshot.Element.FirstOrDefault(e => e.Path == "Patient.address.country");
            Assert.IsNotNull(elem);
            Assert.AreEqual("land", elem.Alias.FirstOrDefault());
            baseElem = elem.Annotation<BaseDefAnnotation>()?.BaseElementDefinition;
            Assert.IsNotNull(baseElem);
            Assert.AreEqual("land", baseElem.Alias.FirstOrDefault());
        }

        private void dumpBaseDefId(StructureDefinition sd)
        {
            Debug.Print("===== " + sd.Name);
            Debug.Print($"{"Path",50}| {"Base Path",49}| {"Base StructureDefinition",69}| {"Element Id",49}| {"Base Element Id",49}");
            foreach (var elem in sd.Snapshot.Element)
            {
                var ann = elem.Annotation<BaseDefAnnotation>();
                Assert.IsNotNull(ann);
                var s49 = new string(' ', 49);
                var s69 = new string(' ', 69);
                Debug.Print($"{elem.Path,50}| {ann?.BaseElementDefinition?.Path?.PadRight(49) ?? s49}| {ann?.BaseStructureDefinition?.Url?.PadRight(69) ?? s69}| {elem?.ElementId?.PadRight(49) ?? s49}| {ann?.BaseElementDefinition?.ElementId?.PadRight(49) ?? s49}");
                var elemId = elem.ElementId;
                Assert.IsNotNull(elemId);
                Assert.IsTrue(elem.IsRootElement() ? elemId == sd.Type : elemId.StartsWith(sd.Type + "."));
            }
        }

        // [WMR 20170524] Added to fix bug reported by Stefan Lang

        // [WMR 20170424] For debugging ElementIdGenerator

        private const string PatientIdentifierProfileUri = @"http://example.org/fhir/StructureDefinition/PatientIdentifierProfile";
        private const string PatientProfileWithIdentifierProfileUri = @"http://example.org/fhir/StructureDefinition/PatientProfileWithIdentifierProfile";
        private const string PatientIdentifierTypeValueSetUri = @"http://example.org/fhir/ValueSet/PatientIdentifierTypeValueSet";

        // Identifier profile with valueset binding on child element Identifier.type
        private static StructureDefinition PatientIdentifierProfile => new()
        {
            Type = FHIRAllTypes.Identifier.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Identifier),
            Name = "PatientIdentifierProfile",
            Url = PatientIdentifierProfileUri,
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.ComplexType,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Identifier.type")
                    {
                        Min = 1,
                        Binding = new ElementDefinition.ElementDefinitionBindingComponent()
                        {
                            Strength = BindingStrength.Extensible,
                            ValueSet = PatientIdentifierTypeValueSetUri
                        }
                    },
                }
            }
        };

        // Patient profile with type profile constraint on Patient.identifier
        // Snapshot should pick up the valueset binding on Identifier.type
        private static StructureDefinition PatientProfileWithIdentifierProfile => new()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
            Name = "PatientProfileWithIdentifierProfile",
            Url = PatientProfileWithIdentifierProfileUri,
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.Resource,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Patient.identifier")
                    {
                        Type = new List<ElementDefinition.TypeRefComponent>()
                        {
                            new ElementDefinition.TypeRefComponent()
                            {
                                Code = FHIRAllTypes.Identifier.GetLiteral(),
                                Profile = new string[] { PatientIdentifierProfileUri }
                            }
                        }
                    }
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestTypeProfileWithChildElementBinding()
        {
            var patientProfile = PatientProfileWithIdentifierProfile;
            var resolver = new InMemoryResourceResolver(patientProfile, PatientIdentifierProfile);
            var multiResolver = new MultiResolver(_testResolver, resolver);

            _generator = new SnapshotGenerator(multiResolver, _settings);

            StructureDefinition expanded = null;
            _generator.BeforeExpandElement += beforeExpandElementHandler_DEBUG;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(patientProfile);
            }
            finally
            {
                _generator.BeforeExpandElement -= beforeExpandElementHandler_DEBUG;
            }

            dumpOutcome(_generator.Outcome);
            Assert.IsTrue(expanded.HasSnapshot);
            dumpElements(expanded.Snapshot.Element);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.JumpToFirst("Patient.identifier"));
            Assert.IsNotNull(nav.Current);

            // BUG: binding constraint on Identifier.type is merged onto Patient.identifier...? (parent element!)
            // FIXED [SnapshotGenerator.getSnapshotRootElement] var diffRoot = sd.Differential.GetRootElement();
            Assert.IsNull(nav.Current.Binding);

            // By default, Patient.identifier.type should NOT be included in the generated snapshot
            Assert.IsFalse(nav.MoveToChild("type"));
        }

        private static StructureDefinition QuestionnaireResponseWithSlice => new()
        {
            Type = FHIRAllTypes.QuestionnaireResponse.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.QuestionnaireResponse),
            Name = "QuestionnaireResponseWithSlice",
            Url = @"http://example.org/fhir/StructureDefinition/QuestionnaireResponseWithSlice",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.Resource,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("QuestionnaireResponse.item")
                    {
                        Slicing = new ElementDefinition.SlicingComponent()
                        {
                            Discriminator = new List<ElementDefinition.DiscriminatorComponent>()
                            {
                                new ElementDefinition.DiscriminatorComponent() { Type = ElementDefinition.DiscriminatorType.Value, Path = "text" }
                            }
                        }
                    },
                    new ElementDefinition("QuestionnaireResponse.item")
                    {
                        SliceName = "Q1"
                    },
                    new ElementDefinition("QuestionnaireResponse.item")
                    {
                        SliceName = "Q2"
                    },
                    new ElementDefinition("QuestionnaireResponse.item.linkid")
                    {
                        Max = "0"
                    },
                }
            }
        };

        // Isue #387
        // https://github.com/FirelyTeam/firely-net-sdk/issues/387
        // Cannot reproduce in STU3?
        // [WMR 20170713] Note: in DSTU2, the QuestionnaireResponse core resource definition
        // specifies an example binding on element "QuestionnaireResponse.group.question.answer.value[x]"
        // WITHOUT an actual valueset reference:
        //
        //   <element>
        //     <path value="QuestionnaireResponse.group.question.answer.value[x]"/>
        //     <!-- ... -->
        //     <binding>
        //       <strength value="example"/>
        //       <description value="Code indicating the response provided for a question."/>
        //     </binding>
        //     <!-- ... -->
        //   </element>
        //
        // However in STU3, the core def example binding DOES include a valueset reference.
        [TestMethod]
        public async Tasks.Task TestQRSliceChildrenBindings()
        {
            var sd = QuestionnaireResponseWithSlice;
            var resolver = new InMemoryResourceResolver(sd);
            var multiResolver = new MultiResolver(_testResolver, resolver);

            _generator = new SnapshotGenerator(multiResolver, _settings);

            StructureDefinition expanded = null;
            _generator.BeforeExpandElement += beforeExpandElementHandler_DEBUG;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(sd);
            }
            finally
            {
                _generator.BeforeExpandElement -= beforeExpandElementHandler_DEBUG;
            }

            dumpOutcome(_generator.Outcome);
            Assert.IsTrue(expanded.HasSnapshot);
            dumpElements(expanded.Snapshot.Element);

            // Verify the inherited example binding on QuestionnaireResponse.item.answer.value[x]
            var answerValues = expanded.Snapshot.Element.Where(e => e.Path == "QuestionnaireResponse.item.answer.value[x]").ToList();
            Assert.AreEqual(3, answerValues.Count);
            foreach (var elem in answerValues)
            {
                var binding = elem.Binding;
                Assert.IsNotNull(binding);
                Assert.AreEqual(BindingStrength.Example, binding.Strength);
                var ValueSetReference = binding.ValueSet;
                Assert.IsNotNull(ValueSetReference);
                Assert.AreEqual("http://hl7.org/fhir/ValueSet/questionnaire-answers", ValueSetReference);
                // Assert.IsTrue(ValueSetReference.Url.Equals("http://hl7.org/fhir/ValueSet/questionnaire-answers"));
                var bindingNameExtension = binding.Extension.FirstOrDefault(e => e.Url == "http://hl7.org/fhir/StructureDefinition/elementdefinition-bindingName");
                Assert.IsNotNull(bindingNameExtension);
                var bindingNameValue = bindingNameExtension.Value as FhirString;
                Assert.IsNotNull(bindingNameValue);
                Assert.AreEqual("QuestionnaireAnswer", bindingNameValue.Value);
            }
        }

        // For derived profiles, base element annotations are incorrect
        // https://trello.com/c/8h7u2qRa
        // Three layers of derived profiles: MyVitalSigns => VitalSigns => Observation
        // When expanding MyVitalSigns, the annotated base elements also include local diff constraints... WRONG!
        // As a result, Forge will not detect the existing local constraints (no yellow pen, excluded from output).

        private static StructureDefinition MyDerivedObservation => new()
        {
            Type = FHIRAllTypes.Observation.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation),
            Name = "MyDerivedObservation",
            Url = @"http://example.org/fhir/StructureDefinition/MyDerivedObservation",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.Resource,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Observation.method")
                    {
                        Short = "DerivedMethodShort"
                    }
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestDerivedObservation()
        {
            var derivedObs = MyDerivedObservation;
            var resolver = new InMemoryResourceResolver(derivedObs);
            var multiResolver = new MultiResolver(_testResolver, resolver);

            _generator = new SnapshotGenerator(multiResolver, _settings);

            StructureDefinition expanded = null;
            // _generator.BeforeExpandElement += beforeExpandElementHandler_DEBUG;
            _generator.PrepareElement += elementHandler;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(derivedObs);
            }
            finally
            {
                // _generator.BeforeExpandElement -= beforeExpandElementHandler_DEBUG;
                _generator.PrepareElement -= elementHandler;
            }

            dumpOutcome(_generator.Outcome);
            Assert.IsTrue(expanded.HasSnapshot);
            // dumpElements(expanded.Snapshot.Element);
            dumpBaseElems(expanded.Snapshot.Element);

            var derivedMethodElem = expanded.Snapshot.Element.FirstOrDefault(e => e.Path == "Observation.method");
            Assert.IsNotNull(derivedMethodElem);
            Assert.AreEqual("DerivedMethodShort", derivedMethodElem.Short);

            var coreObs = await _testResolver.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.Observation);
            Assert.IsTrue(coreObs.HasSnapshot);
            var coreMethodElem = coreObs.Snapshot.Element.FirstOrDefault(e => e.Path == "Observation.method");
            Assert.IsNotNull(coreMethodElem);
            Assert.IsNotNull(coreMethodElem.Short);

            var annotation = derivedMethodElem.Annotation<BaseDefAnnotation>();
            Assert.IsNotNull(annotation);
            var baseElem = annotation.BaseElementDefinition;
            Assert.IsNotNull(baseElem);
            Assert.AreEqual(coreMethodElem.Short, baseElem.Short);
        }

        private static StructureDefinition MyMoreDerivedObservation => new()
        {
            Type = FHIRAllTypes.Observation.GetLiteral(),
            BaseDefinition = MyDerivedObservation.Url,
            Name = "MyMoreDerivedObservation",
            Url = @"http://example.org/fhir/StructureDefinition/MyMoreDerivedObservation",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.Resource,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Observation.method")
                    {
                        Short = "MoreDerivedMethodShort",
                        Comment = new Markdown("MoreDerivedMethodComment")
                    },
                    // Include child constraint to force full expansion of .bodySite node
                    // BUG: if we include this element, then the generated base element for .bodySite is incorrect
                    // (includes local constraints, i.e. Min = 1 ... WRONG!)
                    new ElementDefinition("Observation.method.coding.code")
                    {
                        Min = 1
                    },
                }
            }
        };


        [TestMethod]
        public async Tasks.Task TestNamedTypeSlice()
        {
            var derivedObs = MyNamedTypeSlice;
            var resolver = new InMemoryResourceResolver(derivedObs);
            var multiResolver = new MultiResolver(_testResolver, resolver);

            _generator = new SnapshotGenerator(multiResolver, _settings);

            var (_, expanded) = await generateSnapshotAndCompare(derivedObs);

            Assert.IsTrue(expanded.HasSnapshot);
        }


        private static StructureDefinition MyNamedTypeSlice => new()
        {
            Type = FHIRAllTypes.Observation.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation),
            Name = "MyNamedTypeSlice",
            Url = @"http://example.org/fhir/StructureDefinition/MyNamedTypeSlice",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.Resource,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Observation"),
                    new ElementDefinition("Observation.value[x]")
                    {
                        ElementId = "Observation.value[x]:valueString",
                        SliceName = "valueString"
                    }.OfType(FHIRAllTypes.String)
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestMoreDerivedObservation()
        {
            var derivedObs = MyDerivedObservation;
            var moreDerivedObs = MyMoreDerivedObservation;
            var resolver = new InMemoryResourceResolver(derivedObs, moreDerivedObs);
            var multiResolver = new MultiResolver(_testResolver, resolver);

            _generator = new SnapshotGenerator(multiResolver, _settings);

            StructureDefinition expanded = null;
            // _generator.BeforeExpandElement += beforeExpandElementHandler_DEBUG;
            _generator.PrepareElement += elementHandler;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(moreDerivedObs);
            }
            finally
            {
                // _generator.BeforeExpandElement -= beforeExpandElementHandler_DEBUG;
                _generator.PrepareElement -= elementHandler;
            }

            dumpOutcome(_generator.Outcome);
            Assert.IsTrue(expanded.HasSnapshot);
            // dumpElements(expanded.Snapshot.Element);
            dumpBaseElems(expanded.Snapshot.Element);

            var moreDerivedMethodElem = expanded.Snapshot.Element.FirstOrDefault(e => e.Path == "Observation.method");
            Assert.IsNotNull(moreDerivedMethodElem);
            Assert.AreEqual("MoreDerivedMethodShort", moreDerivedMethodElem.Short);

            Assert.IsTrue(derivedObs.HasSnapshot);
            var derivedMethodElem = derivedObs.Snapshot.Element.FirstOrDefault(e => e.Path == "Observation.method");
            Assert.IsNotNull(derivedMethodElem);
            Assert.AreEqual("DerivedMethodShort", derivedMethodElem.Short);

            // MoreDerivedObservation:Observation.method.short is inherited from DerivedObservation:Observation.method.short
            var annotation = moreDerivedMethodElem.Annotation<BaseDefAnnotation>();
            Assert.IsNotNull(annotation);
            var baseElem = annotation.BaseElementDefinition;
            Assert.IsNotNull(baseElem);
            Assert.AreEqual(derivedMethodElem.Short, baseElem.Short);

            // MoreDerivedObservation:Observation.method.comments is inherited from Core:Observation.method.comments
            var coreObs = await _testResolver.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.Observation);
            Assert.IsTrue(coreObs.HasSnapshot);
            var coreMethodElem = coreObs.Snapshot.Element.FirstOrDefault(e => e.Path == "Observation.method");
            Assert.IsNotNull(coreMethodElem);
            Assert.IsNotNull(coreMethodElem.CommentElement);
            // [WMR 20181212] R4 - Comment type changed from string to markdown
            Assert.IsTrue(coreMethodElem.CommentElement.IsExactly(baseElem.CommentElement));
            Assert.IsTrue(baseElem.CommentElement.IsExactly(coreMethodElem.CommentElement));
        }

        // [WMR 20170718] Test for slicing issue
        private static StructureDefinition MySlicedDocumentReference => new()
        {
            Type = FHIRAllTypes.Observation.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.DocumentReference),
            Name = "MySlicedDocumentReference",
            Url = "http://example.org/fhir/StructureDefinition/MySlicedDocumentReference",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.Resource,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("DocumentReference.content")
                    {
                        Slicing = new ElementDefinition.SlicingComponent()
                        {
                            Description = "TEST"
                            // Min = 1 in core resource definition
                        }
                    },
                    new ElementDefinition("DocumentReference.content")
                    {
                        SliceName = "meta",
                        // Following should be considered as a constraint!
                        // As named slices should always start with Min = 0
                        Min = 1
                    },
                }
            }
        };

        // https://trello.com/c/d7EuVgZI
        // Named slices should never inherit minimum cardinality from base element.
        // Instead, named slice base should always have Min = 0
        // Only slice entry inherits cardinality from base.
        [TestMethod]
        public async Tasks.Task TestNamedSliceMinCardinality()
        {
            var sd = MySlicedDocumentReference;
            var resolver = new InMemoryResourceResolver(sd);
            var multiResolver = new MultiResolver(_testResolver, resolver);

            _generator = new SnapshotGenerator(multiResolver, _settings);

            StructureDefinition expanded = null;
            // _generator.BeforeExpandElement += beforeExpandElementHandler_DEBUG;
            _generator.PrepareElement += elementHandler;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(sd);
            }
            finally
            {
                // _generator.BeforeExpandElement -= beforeExpandElementHandler_DEBUG;
                _generator.PrepareElement -= elementHandler;
            }

            dumpOutcome(_generator.Outcome);
            Assert.IsTrue(expanded.HasSnapshot);
            // dumpElements(expanded.Snapshot.Element);
            dumpBaseElems(expanded.Snapshot.Element);

            var coreProfile = await _testResolver.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.DocumentReference);
            Assert.IsNotNull(coreProfile);
            Assert.IsTrue(coreProfile.HasSnapshot);

            // Verify slice entry in snapshot
            var elems = expanded.Snapshot.Element;
            var snapSliceEntry = elems.FirstOrDefault(e => e.Path == "DocumentReference.content");
            Assert.IsNotNull(snapSliceEntry);
            Assert.IsNotNull(snapSliceEntry.Slicing);

            // Verify that slice entry inherits min cardinality from base profile
            var coreElem = coreProfile.Snapshot.Element.FirstOrDefault(e => e.Path == snapSliceEntry.Path);
            Assert.IsNotNull(coreElem);
            Assert.AreEqual(1, coreElem.Min);
            Assert.AreEqual(coreElem.Min, snapSliceEntry.Min);

            // Verify that named slices do NOT inherit min cardinality from base profile
            var diffSlice = sd.Differential.Element.FirstOrDefault(e => e.SliceName != null);
            Assert.IsNotNull(diffSlice);
            var snapSlice = elems.FirstOrDefault(e => e.SliceName == diffSlice.SliceName);
            Assert.IsNotNull(snapSlice);
            Assert.AreEqual(diffSlice.Min, snapSlice.Min);
            var sliceBaseAnn = snapSlice.Annotation<BaseDefAnnotation>();
            Assert.IsNotNull(sliceBaseAnn);
            var sliceBase = sliceBaseAnn.BaseElementDefinition;
            Assert.IsNotNull(sliceBase);
            // Verify that slice base always has Min = 0 (not inherited from base profile)
            Assert.AreEqual(0, sliceBase.Min);
        }

        // [WMR 20170718] NEW
        // Accept and handle derived profile constraints on existing slice entry in base profile

        private static StructureDefinition MySlicedBasePatient => new()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
            Name = "MySlicedBasePatient",
            Url = @"http://example.org/fhir/StructureDefinition/MySlicedBasePatient",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.Resource,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Patient.identifier")
                    {
                        Slicing = new ElementDefinition.SlicingComponent()
                        {
                            Description = "TEST"
                        }
                    },
                    new ElementDefinition("Patient.identifier")
                    {
                        SliceName = "bsn"
                    }
                }
            }
        };

        private static StructureDefinition MyMoreDerivedPatient => new()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = MySlicedBasePatient.Url,
            Name = "MyMoreDerivedPatient",
            Url = @"http://example.org/fhir/StructureDefinition/MyMoreDerivedPatient",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.Resource,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    // Further constrain existing slice entry
                    new ElementDefinition("Patient.identifier")
                    {
                        Min = 1
                    }
                }
            }
        };

        // https://trello.com/c/Mnn0EBOg
        [TestMethod]
        public async Tasks.Task TestConstraintOnSliceEntry()
        {
            var sd = MyMoreDerivedPatient;
            var resolver = new InMemoryResourceResolver(sd, MySlicedBasePatient);
            var multiResolver = new MultiResolver(_testResolver, resolver);

            _generator = new SnapshotGenerator(multiResolver, _settings);

            StructureDefinition expanded = null;
            // _generator.BeforeExpandElement += beforeExpandElementHandler_DEBUG;
            _generator.PrepareElement += elementHandler;
            try
            {
                (_, expanded) = await generateSnapshotAndCompare(sd);
            }
            finally
            {
                // _generator.BeforeExpandElement -= beforeExpandElementHandler_DEBUG;
                _generator.PrepareElement -= elementHandler;
            }

            dumpOutcome(_generator.Outcome);
            Assert.IsTrue(expanded.HasSnapshot);
            // dumpElements(expanded.Snapshot.Element);
            dumpBaseElems(expanded.Snapshot.Element);

            // Snapshot generator should NOT emit any issues
            // * Issue #0: Severity = 'Error' Code = 'Required' Details: '10008' Text : 'Element 'Patient.identifier' defines a slice without a name. Individual slices must always have a unique name, except extensions.' Profile: 'http://example.org/fhir/StructureDefinition/MyMoreDerivedPatient' Path: 'Patient.identifier'
            Assert.IsNull(_generator.Outcome);

            // Verify constraint on slice entry
            var elems = expanded.Snapshot.Element;
            var sliceEntry = elems.FirstOrDefault(e => e.Path == "Patient.identifier");
            Assert.IsNotNull(sliceEntry);
            Assert.IsNotNull(sliceEntry.Slicing);
            Assert.AreEqual(1, sliceEntry.Min);
            var ann = sliceEntry.Annotation<BaseDefAnnotation>();
            Assert.IsNotNull(ann);
            Assert.IsNotNull(ann.BaseElementDefinition);
            Assert.AreEqual(0, ann.BaseElementDefinition.Min);
        }

        // [WMR 20170810] https://trello.com/c/KNMYa44V
        [TestMethod]
        public async Tasks.Task TestDosage()
        {
            // Note: resolved from TestData\snapshot-test\profiles-types.xml
            var sd = await _testResolver.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.Dosage);
            _generator = new SnapshotGenerator(_testResolver, _settings);

            var (_, expanded) = await generateSnapshotAndCompare(sd);
            dumpOutcome(_generator.Outcome);
            Assert.IsTrue(expanded.HasSnapshot);
            var elems = expanded.Snapshot.Element;
            dumpElements(elems);
            // dumpBaseElems(elems);

            foreach (var elem in elems)
            {
                Assert.IsNull(elem.SliceName, $"Error! Unexpected slice name '{elem.SliceName}' on element with path '{elem.Path}'");
            }

            // Also verify the expanded snapshot of the referenced SimpleQuantity profile
            sd = await _testResolver.FindStructureDefinitionForCoreTypeAsync("SimpleQuantity");
            Assert.IsNotNull(sd);
            Assert.IsTrue(sd.HasSnapshot);
            Assert.IsNull(sd.Differential.GetRootElement()?.SliceName);

            // Note: depending on the order of unit tests execution, SimpleQuantity snapshot
            // may not have been fully (re-)generated. The original snapshot (from core ZIP)
            // contains the invalid sliceName. Regenerated snapshot should be corrected.
            if (sd.Snapshot.IsCreatedBySnapshotGenerator())
            {
                Assert.IsNull(sd.Snapshot.GetRootElement()?.SliceName);
            }
        }


        [TestMethod]
        public async Tasks.Task TestSimpleQuantitySlice()
        {
            var sd = MedicationUsageWithSimpleQuantitySlice;
            var resolver = new InMemoryResourceResolver(sd);
            var multiResolver = new MultiResolver(_testResolver, resolver);

            _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, StructureDefinition expanded) = await generateSnapshotAndCompare(sd);
            dumpOutcome(_generator.Outcome);
            Assert.IsTrue(expanded.HasSnapshot);
            var elems = expanded.Snapshot.Element;
            dumpElements(elems);
            // dumpBaseElems(elems);

            var issues = _generator.Outcome?.Issue.Where(i => i.Details.Coding.FirstOrDefault().Code == SnapshotGenerator.PROFILE_ELEMENTDEF_INVALID_PROFILE_TYPE.Code.ToString());

            // Verify there is NO warning about invalid element type constraint
            Assert.IsTrue(issues == null || !issues.Any());

        }

        // [WMR 20170925] BUG: Stefan Lang - Forge displays both valueString and value[x]
        // https://trello.com/c/XI8krV6j

        private const string SL_HumanNameTitleSuffixUri = @"http://example.org/fhir/StructureDefinition/SL-HumanNameTitleSuffix";

        // Extension on complex datatype HumanName
        private static StructureDefinition SL_HumanNameTitleSuffix => new()
        {
            Type = FHIRAllTypes.Extension.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Extension),
            Name = "SL-HumanNameTitleSuffix",
            Url = SL_HumanNameTitleSuffixUri,
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.ComplexType,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Extension.url")
                    {
                        Fixed = new FhirUri(SL_HumanNameTitleSuffixUri)
                    },
                    // Constrain type to string
                    new ElementDefinition("Extension.valueString")
                    {
                        Short = "NameSuffix",
                        Type = new List<ElementDefinition.TypeRefComponent>()
                        {
                            new ElementDefinition.TypeRefComponent()
                            {
                                Code = FHIRAllTypes.String.GetLiteral()
                            }
                        }
                    }
                }
            }
        };

        // Profile on complex datatype HumanName with extension element
        private static StructureDefinition SL_HumanNameBasis => new()
        {
            Type = FHIRAllTypes.HumanName.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.HumanName),
            Name = "SL-HumanNameBasis",
            Url = @"http://example.org/fhir/StructureDefinition/SL-HumanNameBasis",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.ComplexType,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("HumanName.family.extension")
                    {
                        SliceName = "NameSuffix",
                        Max = "1",
                        Type = new List<ElementDefinition.TypeRefComponent>()
                        {
                            new ElementDefinition.TypeRefComponent()
                            {
                                Code = FHIRAllTypes.Extension.GetLiteral(),
                                Profile = new string[] { SL_HumanNameTitleSuffix.Url }
                            }
                        }
                    },
                }
            }
        };

        // Profile on Patient referencing custom HumanName datatype profile
        private static StructureDefinition SL_PatientBasis => new()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
            Name = "SL-PatientBasis",
            Url = @"http://example.org/fhir/StructureDefinition/SL-PatientBasis",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.Resource,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Patient.name")
                    {
                        Type = new List<ElementDefinition.TypeRefComponent>()
                        {
                            new ElementDefinition.TypeRefComponent()
                            {
                                Code = FHIRAllTypes.HumanName.GetLiteral(),
                                Profile = new string[] { SL_HumanNameBasis.Url }
                            }
                        }
                    },
                }
            }
        };

        private const string SL_NameSuffixValueSetUri = @"http://fhir.de/ValueSet/deuev/anlage-7-namenszusaetze";

        // Derived profile on Patient
        private static StructureDefinition SL_PatientDerived => new()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = SL_PatientBasis.Url,
            Name = "SL-PatientDerived",
            Url = @"http://example.org/fhir/StructureDefinition/SL-PatientDerived",
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.Resource,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Patient.name.family.extension")
                    {
                        SliceName = "NameSuffix",
                        MustSupport = true
                    },
                    // WRONG! Derived profiles must maintain name of inherited renamed elements
                    // => SnapshotGenerator should emit a warning
                    // new ElementDefinition("Patient.name.family.extension.value[x]")
                    // CORRECT
                    new ElementDefinition("Patient.name.family.extension.valueString")
                    {
                        Binding = new ElementDefinition.ElementDefinitionBindingComponent()
                        {
                            Strength = BindingStrength.Required,
                            ValueSet = SL_NameSuffixValueSetUri
                        }
                    }
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestPatientDe()
        {
            var sd = SL_PatientDerived;
            var resolver = new InMemoryResourceResolver(sd, SL_PatientBasis, SL_HumanNameBasis, SL_HumanNameTitleSuffix);
            var multiResolver = new MultiResolver(_testResolver, resolver);

            _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(sd);
            dumpOutcome(_generator.Outcome);
            Assert.IsTrue(expanded.HasSnapshot);
            var elems = expanded.Snapshot.Element;
            dumpElements(elems);
            // dumpBaseElems(elems);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            // Verify slice entry
            Assert.IsTrue(nav.JumpToFirst("Patient.name.family.extension"));
            Assert.IsNotNull(nav.Current.Slicing);
            Assert.IsNull(nav.Current.SliceName);
            // Verify first extension slice: NameSuffix
            Assert.IsTrue(nav.MoveToNextSlice());
            Assert.AreEqual("NameSuffix", nav.Current.SliceName);
            // Verify constraint inherited from base patient profile
            Assert.AreEqual("1", nav.Current.Max);
            // Verify constraint specified by derived patient profile
            Assert.AreEqual(true, nav.Current.MustSupport);
            Assert.IsTrue(nav.MoveToFirstChild());
            // Verify constraints on url child element inherited from extension definition
            Assert.IsTrue(nav.MoveToNext("url"));
            var url = nav.Current.Fixed as FhirUri;
            Assert.IsNotNull(url);
            Assert.AreEqual(SL_HumanNameTitleSuffixUri, url.Value);

            // [WMR 20190211] FIXED
            // STU3: Verify there are no constraints on value[x]
            //Assert.IsFalse(nav.MoveToNext("value[x]"));
            // R4: snapshot includes both "value[x]" and "valueString"
            Assert.IsTrue(nav.MoveToNext("value[x]"));

            // Verify merged constraints on valueString
#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.MoveToNextSlice("valueString"));
#else
            Assert.IsTrue(nav.MoveToNext("valueString"));
#endif
            Assert.AreEqual("NameSuffix", nav.Current.Short);
            Assert.AreEqual(1, nav.Current.Type.Count);
            Assert.AreEqual(FHIRAllTypes.String.GetLiteral(), nav.Current.Type[0].Code);
            Assert.IsNotNull(nav.Current.Binding);
            Assert.AreEqual(BindingStrength.Required, nav.Current.Binding.Strength);
            var vsUrl = nav.Current.Binding.ValueSet;
            Assert.AreEqual(SL_NameSuffixValueSetUri, vsUrl);
        }

        // [WMR 20170927] ContentReference
        // Observation.component.referenceRange => Observation.referenceRange
        // https://trello.com/c/p1RbTjwi
        [TestMethod]
        public async Tasks.Task TestObservationComponentReferenceRange()
        {
            var sd = new StructureDefinition()
            {
                Url = "http://example.org/fhir/StructureDefinition/ObservationWithComponentReferenceRange",
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation),
                Type = FHIRAllTypes.Observation.GetLiteral(),
                Name = "ObservationWithComponentReferenceRange",
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        // Specify a child constraint on Observation.component.referenceRange
                        // in order to force child element expansion
                        new ElementDefinition("Observation.component.referenceRange.low")
                        {
                            Min = 1,
                            Fixed = new Quantity()
                            {
                                Value = 1.0m
                            }
                        }
                    }
                }
            };

            var resolver = new InMemoryResourceResolver(sd);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);
            var (_, expanded) = await generateSnapshotAndCompare(sd);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            // Expecting single issue about invalid sliceName on SimpleQuantity root (error in core spec)
            dumpOutcome(_generator.Outcome);
            // Assert.IsNotNull(_generator.Outcome);
            // Assert.AreEqual(1, _generator.Outcome.Issue.Count);

            // [WMR 20180115] NEW - Use alternative (iterative) approach for full expansion
            var issues = new List<OperationOutcome.IssueComponent>();
            expanded.Snapshot.Element = await fullyExpand(expanded.Snapshot.Element, issues);
            dumpIssues(issues);
            // Assert.AreEqual(1, issues.Count);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            // Verify inherited constraints on Observation.component.referenceRange.low
            Assert.IsTrue(nav.JumpToFirst("Observation.component.referenceRange.low"));
            // Verify inherited cardinality constraint { min = 1 }
            Assert.AreEqual(1, nav.Current.Min);
            // Verify inherited fixed value constraint { fixedDecimal = 1.0 }
            Assert.IsNotNull(nav.Current.Fixed);
            var q = nav.Current.Fixed as Quantity;
            Assert.IsNotNull(q);
            Assert.AreEqual(1.0m, q.Value);
        }

        // https://trello.com/c/pA4uF7IR
        [TestMethod]
        public async Tasks.Task TestInheritedDataTypeProfileExtensions()
        {
            var sdHumanNameExtension = new StructureDefinition()
            {
                Url = "http://example.org/fhir/StructureDefinition/HumanNameExtension",
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Extension),
                Type = FHIRAllTypes.Extension.GetLiteral(),
                Name = "HumanNameExtension",
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.ComplexType,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Extension.valueString")
                        {
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.String.GetLiteral()
                                }
                            }
                        }
                    }
                }
            };

            var sdHumanName = new StructureDefinition()
            {
                Url = "http://example.org/fhir/StructureDefinition/HumanNameWithExtension",
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.HumanName),
                Type = FHIRAllTypes.HumanName.GetLiteral(),
                Name = "HumanNameWithExtension",
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.ComplexType,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("HumanName.extension")
                        {
                            SliceName = "MyExtension",
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.Extension.GetLiteral(),
                                    Profile = new string[] { sdHumanNameExtension.Url }
                                }

                            }
                        }
                    }
                }
            };

            var sdBasePatient = new StructureDefinition()
            {
                Url = "http://example.org/fhir/StructureDefinition/MyBasePatient",
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
                Type = FHIRAllTypes.Patient.GetLiteral(),
                Name = "MyBasePatient",
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Patient.name")
                        {
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.HumanName.GetLiteral(),
                                    Profile = new string[] { sdHumanName.Url }
                                }
                            }
                        }
                    }
                }
            };

            var sdDerivedPatient = new StructureDefinition()
            {
                Url = "http://example.org/fhir/StructureDefinition/MyDerivedPatient",
                BaseDefinition = sdBasePatient.Url,
                Type = FHIRAllTypes.Patient.GetLiteral(),
                Name = "MyDerivedPatient",
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.Resource
            };

            var resolver = new InMemoryResourceResolver(sdHumanNameExtension, sdHumanName, sdBasePatient, sdDerivedPatient);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);
            (_, var expanded) = await generateSnapshotAndCompare(sdDerivedPatient);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);
            dumpOutcome(_generator.Outcome);
            dumpElements(expanded.Snapshot.Element);
            Assert.IsNull(_generator.Outcome);

            // [WMR 20180115] NEW - Use alternative (iterative) approach for full expansion
            var issues = new List<OperationOutcome.IssueComponent>();
            expanded.Snapshot.Element = await fullyExpand(expanded.Snapshot.Element, issues);
            dumpIssues(issues);
            Assert.AreEqual(0, issues.Count);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.JumpToFirst("Patient.name.extension"));
            Assert.IsNotNull(nav.Current.Slicing);
            Assert.IsNull(nav.Current.SliceName);
            Assert.IsTrue(nav.MoveToNext());
            Assert.AreEqual("MyExtension", nav.Current.SliceName);
        }

        // [WMR 20171004] NEW

        // Verify generated outcome issue for incompatible type profile
        // Also verify that choice type element renaming is not affected
        [TestMethod]
        public async Tasks.Task TestIncompatibleTypeProfile()
        {
            const string extensionUrl = @"http://example.org/fhir/StructureDefinition/ValueReferenceExtension";
            var sd = new StructureDefinition()
            {
                Type = FHIRAllTypes.Extension.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Extension),
                Name = "ValueReferenceExtension",
                Url = extensionUrl,
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.ComplexType,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Extension.url")
                        {
                            Fixed = new FhirUri(extensionUrl)
                        },
                        new ElementDefinition("Extension.valueReference")
                        {
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.Reference.GetLiteral(),
                                    // WRONG! Should be TargetProfile
                                    // Expecting outcome issue about incompatible profile
                                    Profile = new string[] { ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient) }
                                }
                            }
                        }
                    }
                }
            };

            var resolver = new InMemoryResourceResolver(sd);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);
            var (_, expanded) = await generateSnapshotAndCompare(sd);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);
            dumpOutcome(_generator.Outcome);
            dumpElements(expanded.Snapshot.Element);

            Assert.IsNotNull(_generator.Outcome);
            Assert.IsNotNull(_generator.Outcome.Issue);
            Assert.AreEqual(1, _generator.Outcome.Issue.Count);

            assertIssue(_generator.Outcome.Issue[0], SnapshotGenerator.PROFILE_ELEMENTDEF_INVALID_PROFILE_TYPE, extensionUrl, sd.Differential.Element[1].Path);

            // [WMR 20180115] NEW - Use alternative (iterative) approach for full expansion
            var issues = new List<OperationOutcome.IssueComponent>();
            expanded.Snapshot.Element = await fullyExpand(expanded.Snapshot.Element, issues);
            dumpIssues(issues);
            Assert.AreEqual(1, issues.Count);
            assertIssue(issues[0], SnapshotGenerator.PROFILE_ELEMENTDEF_INVALID_PROFILE_TYPE, extensionUrl, sd.Differential.Element[1].Path);

            // Expecting a single warning about incompatible type profile on element Extension.valueSetReference

            // Verify element renaming is not affected
            // Expecting valueReference in snapshot, not value[x]
            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.JumpToFirst("Extension.value[x]"));
            Assert.IsTrue(nav.MoveToNextSlice("valueReference"));
#else
            Assert.IsTrue(nav.JumpToFirst("Extension.valueReference"));
#endif

            // [WMR 20180723] Changed: SnapshotGenerator.getStructureForTypeRef
            // Snapshot generator now also expands type.profile for resource references,
            // even if incorrect, such as in this unit test (referenced profile is on Patient, not on Reference)

            // Verify expansion of child element valueReference.reference
            // Expect expansion of core type profile for ResourceReference
            // Assert.IsTrue(nav.MoveToChild("reference"));

            // Expect expansion of (incorrect!) core type profile for Patient
            Assert.IsTrue(nav.MoveToChild("photo"));
        }

        // If an element constraint introduces multiple type profiles,
        // then the snapshot generator should not expand profile children.
        // Verify no outcome issue for incompatible type profiles
        // Also verify that choice type element renaming is not affected
        [TestMethod]
        public async Tasks.Task TestMultipleIncompatibleTypeProfiles()
        {
            const string extensionUrl = @"http://example.org/fhir/StructureDefinition/ValueReferenceExtension";
            var sd = new StructureDefinition()
            {
                Type = FHIRAllTypes.Extension.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Extension),
                Name = "ValueReferenceExtension",
                Url = extensionUrl,
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.ComplexType,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Extension.url")
                        {
                            Fixed = new FhirUri(extensionUrl)
                        },
                        new ElementDefinition("Extension.valueReference")
                        {
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.Reference.GetLiteral(),
                                    // WRONG! Should be TargetProfile
                                    // Expecting outcome issue about incompatible profile
                                    Profile = new string[] { ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient) }
                                },
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.Reference.GetLiteral(),
                                    // WRONG! Should be TargetProfile
                                    // Expecting outcome issue about incompatible profile
                                    Profile = new string[] { ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation) }
                                }
                            }
                        }
                    }
                }
            };

            var resolver = new InMemoryResourceResolver(sd);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);
            var (_, expanded) = await generateSnapshotAndCompare(sd);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);
            dumpOutcome(_generator.Outcome);
            dumpElements(expanded.Snapshot.Element);

            // Element specifies multiple type profiles, so snapshot generator will not try to expand
            // Expecting no warnings about incompatible type profiles
            Assert.IsNull(_generator.Outcome);

            // [WMR 20180115] NEW - Use alternative (iterative) approach for full expansion
            var issues = new List<OperationOutcome.IssueComponent>();
            expanded.Snapshot.Element = await fullyExpand(expanded.Snapshot.Element, issues);
            dumpIssues(issues);
            Assert.AreEqual(0, issues.Count);


            // Verify element renaming is not affected
            // Expecting valueReference in snapshot, not value[x]
            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.JumpToFirst("Extension.value[x]"));
            Assert.IsTrue(nav.MoveToNextSlice("valueReference"));
#else
            Assert.IsTrue(nav.JumpToFirst("Extension.valueReference"));
#endif
            // Verify expansion of child element valueReference.reference
            // Expect expansion of core type profile for ResourceReference
            Assert.IsTrue(nav.MoveToChild("reference"));
        }

        // Verify that choice type elements constrained to a single type code are properly renamed,
        // even if there are multiple type options (with same code)
        // https://trello.com/c/OvQFRdCJ
        [TestMethod]
        public async Tasks.Task TestExtensionValueReferenceRenaming()
        {
            const string extensionUrl = @"http://example.org/fhir/StructureDefinition/ValueReferenceExtension";
            var sd = new StructureDefinition()
            {
                Type = FHIRAllTypes.Extension.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Extension),
                Name = "ValueReferenceExtension",
                Url = extensionUrl,
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.ComplexType,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Extension.url")
                        {
                            Fixed = new FhirUri(extensionUrl)
                        },
                        new ElementDefinition("Extension.valueReference")
                        {
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.Reference.GetLiteral(),
                                    TargetProfile = new string[] { ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient) }
                                },
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.Reference.GetLiteral(),
                                    TargetProfile = new string[] { ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation) }
                                }
                            }
                        }
                    }
                }
            };

            var resolver = new InMemoryResourceResolver(sd);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);
            var (_, expanded) = await generateSnapshotAndCompare(sd);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);
            dumpOutcome(_generator.Outcome);
            dumpElements(expanded.Snapshot.Element);

            Assert.IsNull(_generator.Outcome);

            // [WMR 20180115] NEW - Use alternative (iterative) approach for full expansion
            var issues = new List<OperationOutcome.IssueComponent>();
            expanded.Snapshot.Element = await fullyExpand(expanded.Snapshot.Element, issues);
            dumpIssues(issues);
            Assert.AreEqual(0, issues.Count);

            // Expecting valueReference in snapshot, not value[x]
            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.JumpToFirst("Extension.value[x]"));
            Assert.IsTrue(nav.MoveToNextSlice("valueReference"));
#else
            Assert.IsTrue(nav.JumpToFirst("Extension.valueReference"));
#endif
            // Verify expansion of child element valueReference.reference
            // Expect expansion of core type profile for ResourceReference
            Assert.IsTrue(nav.MoveToChild("reference"));
        }

        /// <summary>
        /// Test if a bundle entry resource is expanded.
        /// </summary>
        /// <param name="fhirType">The resource type to add to the test bundle.</param>
        /// <param name="profileCanonical">Optional canonical for the profile (to be used with FHIRAllTypes.Resource).</param>
        /// <param name="differentialElement">Optional element name to add to the differential (min = 1).</param>
        /// <param name="alwaysExpand">Flag indicating if the bundle entry resource should always be expanded.</param>
        [DataTestMethod]
        [DataRow(FHIRAllTypes.Resource, "", "", false)]
        [DataRow(FHIRAllTypes.Resource, "", "", true)]
        [DataRow(FHIRAllTypes.Resource, "", "id", false)]
        [DataRow(FHIRAllTypes.Resource, "", "id", true)]
        [DataRow(FHIRAllTypes.Resource, "http://hl7.org/fhir/StructureDefinition/List", "", false)]
        [DataRow(FHIRAllTypes.Resource, "http://hl7.org/fhir/StructureDefinition/List", "", true)]
        [DataRow(FHIRAllTypes.Resource, "http://hl7.org/fhir/StructureDefinition/List", "orderedBy", false)]
        [DataRow(FHIRAllTypes.Resource, "http://hl7.org/fhir/StructureDefinition/List", "orderedBy", true)]
        [DataRow(FHIRAllTypes.List, "", "", false)]
        [DataRow(FHIRAllTypes.List, "", "", true)]
        [DataRow(FHIRAllTypes.List, "", "orderedBy", false)]
        [DataRow(FHIRAllTypes.List, "", "orderedBy", true)]
        [DataRow(FHIRAllTypes.Resource, "http://hl7.org/fhir/StructureDefinition/Patient", "", false)]
        [DataRow(FHIRAllTypes.Resource, "http://hl7.org/fhir/StructureDefinition/Patient", "", true)]
        [DataRow(FHIRAllTypes.Resource, "http://hl7.org/fhir/StructureDefinition/Patient", "gender", false)]
        [DataRow(FHIRAllTypes.Resource, "http://hl7.org/fhir/StructureDefinition/Patient", "gender", true)]
        [DataRow(FHIRAllTypes.Patient, "", "", false)]
        [DataRow(FHIRAllTypes.Patient, "", "", true)]
        [DataRow(FHIRAllTypes.Patient, "", "gender", false)]
        [DataRow(FHIRAllTypes.Patient, "", "gender", true)]
        public async Tasks.Task TestExpandBundleEntryResource(FHIRAllTypes fhirType, string profileCanonical, string differentialElement, bool alwaysExpand)
        {
            const string BundleEntryResource = "Bundle.entry.resource";

            void OnBeforeExpandElement(object sender, SnapshotExpandElementEventArgs e)
            {
                if (e.Element.Path == BundleEntryResource)
                {
                    if (!string.IsNullOrEmpty(differentialElement))
                    {
                        Assert.IsTrue(e.MustExpand);
                        Assert.IsTrue(e.HasChildren);
                    }
                    else
                    {
                        Assert.AreEqual(e.MustExpand, e.HasChildren);
                    }

                    if (alwaysExpand)
                        e.MustExpand = true;
                }
                else
                {
                    Assert.AreEqual(e.MustExpand, e.HasChildren);
                }
            }

            // Verify that the snapshot generator is capable of expanding Bundle.entry.resource,
            // if constrained to a resource type

            var sd = new StructureDefinition()
            {
                Type = FHIRAllTypes.Bundle.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Bundle),
                Name = "BundleWithType",
                Url = @"http://example.org/fhir/StructureDefinition/BundleWithType",
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition(BundleEntryResource)
                        {
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = fhirType.GetLiteral(),
                                    ProfileElement = string.IsNullOrEmpty(profileCanonical) ? null : new List<Canonical> { new Canonical(profileCanonical) }
                                }
                            }
                        },
                    }
                }
            };

            if (!string.IsNullOrEmpty(differentialElement))
                sd.Differential.Element.Add(new ElementDefinition($"{BundleEntryResource}.{differentialElement}") { Min = 1 });

            var resolver = new InMemoryResourceResolver(sd);
            var multiResolver = new MultiResolver(resolver, _testResolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            Debug.Print("===== Prepare ===== ");
            // Prepare standard snapshots for core Bundle & the specified fhir type

            var sdBundle = await _testResolver.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.Bundle);
            Assert.IsNotNull(sdBundle);
            await _generator.UpdateAsync(sdBundle);
            Assert.IsTrue(sdBundle.HasSnapshot);

            var sdType = await _testResolver.FindStructureDefinitionForCoreTypeAsync(fhirType);
            Assert.IsNotNull(sdType);
            await _generator.UpdateAsync(sdType);
            Assert.IsTrue(sdType.HasSnapshot);

            Debug.Print("===== Generate ===== ");
            // Generate custom snapshot for Bundle profile

            _generator.PrepareElement += elementHandler;
            _generator.BeforeExpandElement += OnBeforeExpandElement;
            try
            {
                var (_, expanded) = await generateSnapshotAndCompare(sd);

                dumpOutcome(_generator.Outcome);
                Assert.IsTrue(expanded.HasSnapshot);
                dumpBaseElems(expanded.Snapshot.Element);

                // Snapshot generator should NOT emit any issues
                Assert.IsNull(_generator.Outcome);

                var elems = expanded.Snapshot.Element;

                // The snapshot generator should fully expand resource children if the resource is different from
                // the base resource or when we override the behaviour in the event BeforeExpandElement.
                var issues = _generator.Outcome?.Issue ?? new List<OperationOutcome.IssueComponent>();
                Assert.AreEqual(0, issues.Count);

                // Verify that Bundle.entry.resource : fhir type was properly expanded (or not)
                var expectExpanded = !string.IsNullOrEmpty(differentialElement) || alwaysExpand;
                var pos = elems.FindIndex(e => e.Path == BundleEntryResource);
                Assert.AreNotEqual(-1, pos);
                var elem = elems[pos];
                Assert.AreEqual(fhirType.GetLiteral(), elem.Type.FirstOrDefault()?.Code);

                // Verify that expanded child elements of Bundle.entry.resource
                var listElems = sdType.Snapshot.Element;
                for (int i = 1; i < listElems.Count; i++)
                {
                    var listElem = listElems[i];
                    var rebasedPath = ElementDefinitionNavigator.ReplacePathRoot(listElem.Path, BundleEntryResource);
                    pos = elems.FindIndex(pos + 1, e => e.Path == rebasedPath);

                    // Verify bundle entry resource expansion in snapshot
                    if (expectExpanded)
                        Assert.AreNotEqual(-1, pos); // Should contain element
                    else
                        Assert.AreEqual(-1, pos); // Should not contain element
                }
            }
            finally
            {
                _generator.BeforeExpandElement -= OnBeforeExpandElement;
                _generator.PrepareElement -= elementHandler;
            }
        }

        // [WMR 20180115]
        // https://github.com/FirelyTeam/firely-net-sdk/issues/510
        // "Missing diff annotation on ElementDefinition.TypeRefComponent"
        [TestMethod]
        public async Tasks.Task TestConstrainedByDiff_Type()
        {
            StructureDefinition sd = new StructureDefinition()
            {
                Type = FHIRAllTypes.Patient.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
                Name = "MyNationalPatient",
                Url = "http://example.org/fhir/StructureDefinition/MyNationalPatient",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Patient.name")
                        {
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Profile = new string[] { @"http://fhir.nl/fhir/StructureDefinition/nl-core-humanname" }
                                }
                            }
                        },
                        new ElementDefinition("Patient.generalPractitioner")
                        {
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    TargetProfile = new string[] { @"http://fhir.nl/fhir/StructureDefinition/nl-core-organization" }
                                },
                                new ElementDefinition.TypeRefComponent()
                                {
                                    TargetProfile = new string[] { @"http://fhir.nl/fhir/StructureDefinition/nl-core-practitioner" }
                                }
                            }
                        }
                    }
                }
            };

            // Enable annotations on snapshot elements with diff constraints
            var settings = new SnapshotGeneratorSettings(_settings)
            {
                GenerateAnnotationsOnConstraints = true
            };
            _generator = new SnapshotGenerator(_testResolver, settings);

            (_, var expanded) = await generateSnapshotAndCompare(sd);

            dumpOutcome(_generator.Outcome);
            dumpBasePaths(expanded);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.JumpToFirst("Patient.name"));
            Assert.IsTrue(hasChanges(nav.Current));
            Assert.IsFalse(isChanged(nav.Current));
            Assert.IsTrue(hasChanges(nav.Current.Type));
            foreach (var type in nav.Current.Type)
            {
                Assert.IsTrue(isChanged(type));
            }

            Assert.IsTrue(nav.JumpToFirst("Patient.generalPractitioner"));
            Assert.IsTrue(hasChanges(nav.Current));
            Assert.IsFalse(isChanged(nav.Current));
            Assert.IsTrue(hasChanges(nav.Current.Type));
            foreach (var type in nav.Current.Type)
            {
                Assert.IsTrue(isChanged(type));
            }
        }

        [TestMethod]
        public async Tasks.Task TestAuPatientWithExtensions()
        {
            // Forge issue: https://trello.com/c/Q13pabzq

            var sd = await _testResolver.FindStructureDefinitionAsync(@"http://hl7.org.au/fhir/StructureDefinition/au-patient");
            Assert.IsNotNull(sd);

            (_, var expanded) = await generateSnapshotAndCompare(sd);

            dumpOutcome(_generator.Outcome);
            dumpBaseElems(expanded.Snapshot.Element);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            Assert.IsNull(_generator.Outcome);

            // Verify extensions on Patient.birthDate
            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.JumpToFirst("Patient.birthDate.extension"));
            // 1. Extension slice intro
            Assert.IsNotNull(nav.Current.Slicing);
            Assert.IsNull(nav.Current.SliceName);
            // 2. Extension: accuracyIndicator
            Assert.IsTrue(nav.MoveToNextSlice());
            Assert.AreEqual("accuracyIndicator", nav.Current.SliceName);
            // 3. Extension: birthTime
            Assert.IsTrue(nav.MoveToNextSlice());
            Assert.AreEqual("birthTime", nav.Current.SliceName);

            // Verify extensions on Patient.deceased[x]:deceasedDateTime
            Assert.IsTrue(nav.JumpToFirst("Patient.deceased[x]"));
            // 1. Type slice intro
            Assert.IsNotNull(nav.Current.Slicing);
            Assert.IsNull(nav.Current.SliceName);
            // 2. Type slice: deceasedBoolean
            Assert.IsTrue(nav.MoveToNextSlice());
            Assert.AreEqual("deceasedBoolean", nav.Current.SliceName);
            // 3. Type slice: deceasedDateTime
            Assert.IsTrue(nav.MoveToNextSlice());
            Assert.AreEqual("deceasedDateTime", nav.Current.SliceName);
            // 4. Patient.deceased[x]:deceasedDateTime.extension slice intro
            Assert.IsTrue(nav.MoveToChild("extension"));
            Assert.IsNotNull(nav.Current.Slicing);
            Assert.IsNull(nav.Current.SliceName);
            // 5. Extension: accuracyIndicator
            Assert.IsTrue(nav.MoveToNextSlice());
            Assert.AreEqual("accuracyIndicator", nav.Current.SliceName);
        }

        // [WMR 20180410] Unit test to investigate issue reported by David McKillop
        [TestMethod]
        public async Tasks.Task TestAuPatientDerived()
        {
            var sd = new StructureDefinition()
            {
                Type = FHIRAllTypes.Patient.GetLiteral(),
                BaseDefinition = @"http://hl7.org.au/fhir/StructureDefinition/au-patient",
                Name = "AuPatientDerived",
                Url = "http://example.org/fhir/StructureDefinition/AuPatientDerived",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Patient.deceased[x]")
                        {
                            MustSupport = true
                        },
                        new ElementDefinition("Patient.deceased[x]")
                        {
                            SliceName = "deceasedBoolean",
                            MustSupport = true
                        },
                        new ElementDefinition("Patient.deceased[x]")
                        {
                            SliceName = "deceasedDateTime",
                            MustSupport = true
                        },
                        new ElementDefinition("Patient.deceased[x].extension")
                        {
                            SliceName = "accuracyIndicator",
                            MustSupport = true
                        }
                    }
                }

            };

            (_, var expanded) = await generateSnapshotAndCompare(sd);

            dumpOutcome(_generator.Outcome);
            dumpBaseElems(expanded.Snapshot.Element);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            Assert.IsNull(_generator.Outcome);
        }

        // [WMR 20180410] Cannot handle invalid (!) choice type element renaming within type slice
        // Exception from ElementMatcher.matchBase - choiceNames.SingleOrDefault()
        // TODO: Gracefully handle multiple matches, emit issue, use first match
        [Ignore]
        [TestMethod]
        public async Tasks.Task TestAuPatientDerived2()
        {
            var sd = new StructureDefinition()
            {
                Type = FHIRAllTypes.Patient.GetLiteral(),
                BaseDefinition = @"http://hl7.org.au/fhir/StructureDefinition/au-patient",
                Name = "AuPatientDerived2",
                Url = "http://example.org/fhir/StructureDefinition/AuPatientDerived2",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Patient.deceased[x]")
                        {
                            MustSupport = true
                        },
                        new ElementDefinition("Patient.deceasedBoolean]")
                        {
                            SliceName = "deceasedBoolean",
                            MustSupport = true
                        },
                        new ElementDefinition("Patient.deceasedDateTime")
                        {
                            SliceName = "deceasedDateTime",
                            MustSupport = true
                        },
                        new ElementDefinition("Patient.deceasedDateTime.extension")
                        {
                            SliceName = "accuracyIndicator",
                            MustSupport = true
                        }
                    }
                }

            };

            (_, var expanded) = await generateSnapshotAndCompare(sd);

            dumpOutcome(_generator.Outcome);
            dumpBaseElems(expanded.Snapshot.Element);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            Assert.IsNull(_generator.Outcome);
        }

        // [WMR 20180410] Add unit tests for content references

        public StructureDefinition QuestionnaireWithNestedItems = new()
        {
            Type = FHIRAllTypes.Questionnaire.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Questionnaire),
            Name = "QuestionnaireWithNestedItems",
            Url = "http://example.org/fhir/StructureDefinition/QuestionnaireWithNestedItems",
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Questionnaire.item.type")
                        {
                            Short = "level 1"
                        },
                        new ElementDefinition("Questionnaire.item.item.type")
                        {
                            Comment = new Markdown("level 2")
                        }
                    }
            }
        };

        [TestMethod]
        public async Tasks.Task TestContentReferenceQuestionnaire()
        {
            var sd = QuestionnaireWithNestedItems;

            (_, var expanded) = await generateSnapshotAndCompare(sd);

            dumpOutcome(_generator.Outcome);
            dumpBaseElems(expanded.Snapshot.Element);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            Assert.IsNull(_generator.Outcome);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.JumpToFirst("Questionnaire.item.type"));
            Assert.AreEqual("level 1", nav.Current.Short);

            Assert.IsTrue(nav.JumpToFirst("Questionnaire.item.item.type"));
            // [WMR 20181212] R4 - Comment type changed from string to markdown
            Assert.AreEqual("level 2", nav.Current.Comment);
            // Level 2 should NOT inherit constraints from level 1
            Assert.AreNotEqual("level 1", nav.Current.Short);
        }

        [TestMethod]
        public async Tasks.Task TestContentReferenceQuestionnaireDerived()
        {
            var sd = new StructureDefinition
            {
                Type = FHIRAllTypes.Questionnaire.GetLiteral(),
                BaseDefinition = QuestionnaireWithNestedItems.Url,
                Name = "QuestionnaireWithNestedItemsDerived",
                Url = "http://example.org/fhir/StructureDefinition/QuestionnaireWithNestedItemsDerived",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Questionnaire.item.type")
                        {
                            Comment = new Markdown("level 1 *")
                        },
                        new ElementDefinition("Questionnaire.item.item.type")
                        {
                            Short = "level 2 *"
                        }
                    }
                }
            };

            var resolver = new InMemoryResourceResolver(sd, QuestionnaireWithNestedItems);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(sd);

            dumpOutcome(_generator.Outcome);
            dumpBaseElems(expanded.Snapshot.Element);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            Assert.IsNull(_generator.Outcome);

            // Constraints should be merged separately on each nesting level
            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.JumpToFirst("Questionnaire.item.type"));
            Assert.AreEqual("level 1", nav.Current.Short);
            // [WMR 20181212] R4 - Comment type changed from string to markdown
            Assert.AreEqual("level 1 *", nav.Current.Comment);

            Assert.IsTrue(nav.JumpToFirst("Questionnaire.item.item.type"));
            // [WMR 20181212] R4 - Comment type changed from string to markdown
            Assert.AreEqual("level 2", nav.Current.Comment);
            Assert.AreEqual("level 2 *", nav.Current.Short);
        }

        // [WMR 20180604] Issue #611
        // https://github.com/FirelyTeam/firely-net-sdk/issues/611

        [TestMethod]
        public async Tasks.Task TestSnapshotForDerivedSlice()
        {
            var sdBase = new StructureDefinition
            {
                Type = FHIRAllTypes.PractitionerRole.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.PractitionerRole),
                Name = "BasePractitionerRole",
                Url = "http://example.org/fhir/StructureDefinition/BasePractitionerRole",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("PractitionerRole.identifier")
                        {
                            Slicing = new ElementDefinition.SlicingComponent()
                            {
                                Discriminator = new List<ElementDefinition.DiscriminatorComponent>()
                                {
                                    new ElementDefinition.DiscriminatorComponent()
                                    {
                                        Type = ElementDefinition.DiscriminatorType.Value,
                                        Path = "system"
                                    },
                                },
                            }
                        },
                        new ElementDefinition("PractitionerRole.identifier")
                        {
                            SliceName = "foo",
                            Max = "1",
                        },
                        new ElementDefinition("PractitionerRole.identifier")
                        {
                            SliceName = "bar",
                            Max = "1",
                        },
                        new ElementDefinition("PractitionerRole.identifier")
                        {
                            SliceName = "baz",
                            Max = "1",
                        }
                    }
                }
            };

            var sdDerived = new StructureDefinition()
            {
                Type = FHIRAllTypes.PractitionerRole.GetLiteral(),
                BaseDefinition = sdBase.Url,
                Name = "DerivedPractitionerRole",
                Url = "http://example.org/fhir/StructureDefinition/DerivedPractitionerRole",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("PractitionerRole.identifier")
                        {
                            Min = 1,
                        },
                        new ElementDefinition("PractitionerRole.identifier")
                        {
                            SliceName = "bar",
                            Min = 1,
                        }
                    }
                }
            };

            var resolver = new InMemoryResourceResolver(sdBase, sdDerived);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            var (_, expanded) = await generateSnapshotAndCompare(sdDerived);

            dumpOutcome(_generator.Outcome);
            dumpBaseElems(expanded.Snapshot.Element);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            Assert.IsNull(_generator.Outcome);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);

            Assert.IsTrue(nav.JumpToFirst("PractitionerRole.identifier"));
            Assert.IsNotNull(nav.Current.Slicing);
            Assert.IsNull(nav.Current.SliceName);
            Assert.AreEqual(1, nav.Current.Min);    // Derived profile constraint

            Assert.IsTrue(nav.MoveToNext());
            Assert.AreEqual("PractitionerRole.identifier", nav.Path);
            Assert.IsNull(nav.Current.Slicing);
            Assert.AreEqual("foo", nav.Current.SliceName);

            Assert.IsTrue(nav.MoveToNext());
            Assert.AreEqual("PractitionerRole.identifier", nav.Path);
            Assert.IsNull(nav.Current.Slicing);
            Assert.AreEqual("bar", nav.Current.SliceName);
            Assert.AreEqual(1, nav.Current.Min);    // Derived profile constraint
            Assert.AreEqual("1", nav.Current.Max);  // Base profile constraint

            Assert.IsTrue(nav.MoveToNext());
            Assert.AreEqual("PractitionerRole.identifier", nav.Path);
            Assert.IsNull(nav.Current.Slicing);
            Assert.AreEqual("baz", nav.Current.SliceName);

            Assert.IsTrue(nav.MoveToNext());
            Assert.AreNotEqual("PractitionerRole.identifier", nav.Path);

        }

        [TestMethod]
        public async Tasks.Task TestExtensionOnValueSetBinding()
        {
            var profile = new StructureDefinition()
            {
                Type = FHIRAllTypes.Address.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Address),
                Name = "MyCustomAddress",
                Url = "http://example.org/fhir/StructureDefinition/MyCustomAddress",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Address.use")
                        {
                            Binding = new ElementDefinition.ElementDefinitionBindingComponent
                            {
                                ValueSetElement = new Canonical
                                {
                                    Extension = new List<Extension>{new Extension
                                        {
                                            Url = "http://hl7.org/fhir/StructureDefinition/11179-permitted-value-conceptmap",
                                            Value = new ResourceReference
                                            {
                                                Reference = "http://nictiz.nl/fhir/ConceptMap/AdresSoortCodelijst-to-AddressUse",
                                                Display = "AdresSoortCodelijst-to-AddressUse"
                                            }
                                        }
                                    }
                                }
                            }
                        },
                    }
                }
            };

            _generator = new SnapshotGenerator(_testResolver, _settings);
            (_, var expanded) = await generateSnapshotAndCompare(profile);

            Assert.IsNotNull(expanded?.Snapshot?.Element);
            Assert.IsTrue(expanded.Snapshot.Element.Where(e => e.Path == "Address.use").FirstOrDefault().Binding.ValueSetElement.Extension.Any(e => e.Url == "http://hl7.org/fhir/StructureDefinition/11179-permitted-value-conceptmap"));
            Assert.IsNotNull(expanded.Snapshot.Element.Where(e => e.Path == "Address.use")?.FirstOrDefault()?.Binding?.ValueSetElement?.Value);
        }


        // [WMR 20180611] New: Forge issue "Only first item in code field for element is saved"
        // Issue: if element in diff specifies multiple codes with only display values,
        // then element in snapshot only contains the first code entry.

        [TestMethod]
        public async Tasks.Task TestObservationWithDisplayCodes()
        {
            var sd = new StructureDefinition
            {
                Type = FHIRAllTypes.Observation.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation),
                Name = "ObservationWithDisplayCodes",
                Url = "http://example.org/fhir/StructureDefinition/ObservationWithDisplayCodes",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Observation.code")
                        {
                            Code = new List<Coding>()
                            {
                                new Coding() { Display = "foo" },
                                new Coding() { Display = "bar" }
                            }
                        },
                    }
                }
            };


            var resolver = new InMemoryResourceResolver(sd);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            var generator = _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(sd);

            dumpOutcome(generator.Outcome);
            dumpBaseElems(expanded.Snapshot.Element);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            // Expecting single issue about invalid slice name on SimpleQuantity root element
            //var outcome = generator.Outcome;
            //Assert.AreEqual(1, outcome.Issue.Count);
            //assertIssue(outcome.Issue[0], SnapshotGenerator.PROFILE_ELEMENTDEF_INVALID_SLICENAME_ON_ROOT);
            // [WMR 20181212] R4 FIXED - SimpleQuantity core type definition has been fixed
            Assert.IsNull(generator.Outcome);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsNotNull(nav);
            Assert.IsTrue(nav.JumpToFirst("Observation.code"));
            var elem = nav.Current;
            Assert.IsNotNull(elem);
            // Verify that both codings are included in the snapshot
            Assert.AreEqual(2, elem.Code.Count);
            Assert.AreEqual("foo", elem.Code[0].Display);
            Assert.AreEqual("bar", elem.Code[1].Display);
        }

        [TestMethod]
        public async Tasks.Task TestInvariantsOnValueX()
        {
            var sd = await _testResolver.FindStructureDefinitionAsync("http://hl7.org/fhir/StructureDefinition/MedicationAdministration");

            (_, var expanded) = await generateSnapshotAndCompare(sd);

            dumpOutcome(_generator.Outcome);
            dumpBaseElems(expanded.Snapshot.Element);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.JumpToFirst("MedicationAdministration.dosage.rate[x]"));
            Assert.IsNotNull(nav.Current);

            //verify that rate[x] contains ele-1 but not rat-1
            Assert.IsTrue(nav.Current.Constraint.Any(c => c.Key == "ele-1"));
            Assert.IsFalse(nav.Current.Constraint.Any(c => c.Key == "rat-1"));

        }

        [TestMethod]
        public async Tasks.Task TestReferenceTargetProfile()
        {
            // Verify that the snapshot generator correctly expands elements with a targetProfile (on ResourceReference itself)
            var ReferenceProfile = new StructureDefinition()
            {
                Type = FHIRAllTypes.Reference.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Reference),
                Name = "MyCustomReference",
                Url = "http://example.org/fhir/StructureDefinition/MyCustomReference",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Reference")
                        {
                            Comment = new Markdown("CustomReference")
                        },
                        new ElementDefinition("Reference.reference")
                        {
                            Min = 1
                        },
                    }
                }
            };

            var ReportProfile = new StructureDefinition()
            {
                Type = FHIRAllTypes.DiagnosticReport.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.DiagnosticReport),
                Name = "MyDiagnosticReport",
                Url = "http://example.org/fhir/StructureDefinition/MyDiagnosticReport",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("DiagnosticReport.imagingStudy")
                        {
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.Reference.GetLiteral(),
                                    ProfileElement = new List<Canonical> { new Canonical(ReferenceProfile.Url) },
                                    TargetProfileElement = new List<Canonical> { ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.ImagingStudy) }
                                }
                            }
                        },
                        // Add child element constraint to force expansion
                        //new ElementDefinition("DiagnosticReport.imagingStudy.identifier")
                        //{
                        //    Max = "0"
                        //}
                    }
                }
            };

            var resolver = new InMemoryResourceResolver(ReportProfile, ReferenceProfile);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            var generator = _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(ReportProfile);

            dumpOutcome(generator.Outcome);
            dumpBaseElems(expanded.Snapshot.Element);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            var outcome = generator.Outcome;
            Assert.IsNull(outcome);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.JumpToFirst("DiagnosticReport.imagingStudy"));
            Assert.IsNotNull(nav.Current);
            // Verify that snapshot generator merges constraints from external ReferenceProfile
            // [WMR 20181212] R4 Fixed markdown
            Assert.AreEqual("CustomReference", nav.Current.Comment);
            Assert.IsNotNull(nav.Current.Type);
            Assert.AreEqual(1, nav.Current.Type.Count);
            Assert.AreEqual(FHIRAllTypes.Reference.GetLiteral(), nav.Current.Type[0].Code);
            Assert.AreEqual(ReferenceProfile.Url, nav.Current.Type[0].Profile.First());
            // By default, snapshot generator does not expand children of element DiagnosticReport.imagingStudy
            Assert.IsFalse(nav.HasChildren);

            // Explicitly expand children of element DiagnosticReport.imagingStudy
            Assert.IsTrue(await generator.ExpandElementAsync(nav));
            Assert.IsTrue(nav.HasChildren);
            Assert.IsTrue(nav.MoveToChild("reference"));
            Assert.IsNotNull(nav.Current);
            // Verify profile inherits constraint from external targetProfile on Reference
            Assert.AreEqual(1, nav.Current.Min);
        }

        // Issue #827
        [TestMethod]
        public async Tasks.Task TestPrimitiveSnapshot()
        {
            // Expand core string profile
            // Differential introduces three extensions on string.value:
            // http://hl7.org/fhir/StructureDefinition/structuredefinition-json-type = "string"
            // http://hl7.org/fhir/StructureDefinition/structuredefinition-xml-type = "xsd:string"
            // http://hl7.org/fhir/StructureDefinition/structuredefinition-rdf-type = "xsd:string"
            // Verify that these extensions are included in the snapshot

            var src = _testResolver;
            var generator = _generator = new SnapshotGenerator(src, _settings);
            var stringProfile = await src.FindStructureDefinitionForCoreTypeAsync(FHIRAllTypes.String);
            Assert.IsNotNull(stringProfile);
            (_, var expanded) = await generateSnapshotAndCompare(stringProfile);
            Assert.IsNotNull(expanded);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsNotNull(nav);
            Assert.IsTrue(nav.JumpToFirst("string.value"));
            var elem = nav.Current;
            Assert.IsNotNull(elem);
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);

            // Verify default regular expression
            Assert.IsNotNull(elem.Type[0].Extension);
            Assert.AreEqual(2, elem.Type[0].Extension.Count); // 1: regex extension, 2: fhir-type extension
            var regularExpr = elem.Type[0].Extension.FirstOrDefault(e => e.Url is "http://hl7.org/fhir/StructureDefinition/regex");
            Assert.IsNotNull(regularExpr);
            var extValue = regularExpr.Value as FhirString;
            Assert.IsNotNull(extValue);
#if R5
            Assert.AreEqual(@"^[\s\S]+$", extValue.Value);
#else
            Assert.AreEqual("[ \\r\\n\\t\\S]+", extValue.Value);
#endif
            // Verify fhir-type extension
            var fhirTypeExpr = elem.Type[0].Extension.FirstOrDefault(e => e.Url is "http://hl7.org/fhir/StructureDefinition/structuredefinition-fhir-type");
            Assert.IsNotNull(fhirTypeExpr);
            var typeValue = fhirTypeExpr.Value as FhirUrl;
            Assert.IsNotNull(typeValue);
            Assert.AreEqual("string", typeValue.Value);


            // Verify the 'special' System.String type
            Assert.IsNotNull(elem.Type[0].Code);
            Assert.AreEqual("http://hl7.org/fhirpath/System.String", elem.Type[0].Code);
        }

        [TestMethod]
        public async Tasks.Task TestExtensionsOnPrimitiveValue()
        {
            // #827: Verify that derived profiles inherit extensions on value element of primitive types

            var src = new TestProfileArtifactSource();
            var testResolver = new CachedResolver(
                new MultiResolver(
                    _standardFhirSource,
                    src));
            var generator = _generator = new SnapshotGenerator(testResolver, _settings);

#pragma warning disable CS0618 // Type or member is obsolete
            var obs = src.FindStructureDefinition("http://validationtest.org/fhir/StructureDefinition/MyOrganization2");
#pragma warning restore CS0618 // Type or member is obsolete
            Assert.IsNotNull(obs);
            (_, var expanded) = await generateSnapshotAndCompare(obs);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsNotNull(nav);
            Assert.IsTrue(nav.JumpToFirst("Organization.name.value"));
            var elem = nav.Current;
            Assert.IsNotNull(elem);
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            // [WMR 20190131] WRONG! in R4, primitive value elements have no type code
            //Assert.AreEqual("string", elem.Type[0].Code);
            Assert.IsNull(elem.Type[0].Code);

            // Verify constraint on regular expression extension value
            Assert.IsNotNull(elem.Type[0].Extension);
            Assert.AreEqual(1, elem.Type[0].Extension.Count); // 1: regex extension, 2: fhir-type extension
            var regularExpr = elem.Type[0].Extension.FirstOrDefault(e => e.Url is "http://hl7.org/fhir/StructureDefinition/regex");
            Assert.IsNotNull(regularExpr);
            var extValue = regularExpr.Value as FhirString;
            Assert.IsNotNull(extValue);
            Assert.AreEqual("[A-Z].*", extValue.Value); // Constrained
        }

        // [WMR 20190819] #1067 SnapshotGenerator - support implicit type constraints on renamed elements
        // Example: https://www.hl7.org/fhir/bodyheight.html
        //
        // <element id="Observation.valueQuantity">
        //    <path value="Observation.valueQuantity"/> 
        // </element>
        //
        // The element renaming implies a type constraint on Quantity:
        //
        // <type> 
        //    <code value="Quantity"/> 
        // </type> 
        [TestMethod]
        public async Tasks.Task TestRenamedElementImpliesTypeConstraint()
        {
            StructureDefinition ObservationProfileWithImplicitTypeSlice = new StructureDefinition()
            {
                Type = FHIRAllTypes.Observation.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation),
                Name = nameof(ObservationProfileWithImplicitTypeSlice),
                Url = "http://example.org/fhir/StructureDefinition/ObservationProfileWithImplicitTypeSlice",
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        // Renamed element w/o any constraints implies type constraint
                        new ElementDefinition("Observation.valueQuantity")
                        {
                            // Implied:
                            //Type = new List<ElementDefinition.TypeRefComponent>()
                            //{
                            //    new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.Quantity.GetLiteral() }
                            //}
                        },
                    }
                }
            };

            //var resolver = new InMemoryResourceResolver(ObservationProfileWithImplicitTypeSlice);
            //var multiResolver = new MultiResolver(_testResolver, resolver);
            //_generator = new SnapshotGenerator(multiResolver, _settings);

            var obs = ObservationProfileWithImplicitTypeSlice;
            Assert.IsNotNull(obs);
            (_, var expanded) = await generateSnapshotAndCompare(obs);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            dumpElements(expanded.Snapshot.Element);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());
            Assert.IsTrue(nav.MoveToFirstChild());



            Assert.IsTrue(nav.MoveToNext("value[x]"));
            var elem = nav.Current;
            Assert.IsNotNull(elem.Type);
            // 20200908: R5 +1 because of Attachment is also allowed now.
            Assert.AreEqual(getNumberOfChoiceTypes(typeof(Observation), "value"), elem.Type.Count); // Unconstrained

            // Verify implicit type constraint
#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.MoveToNextSlice("valueQuantity"));
#else
            Assert.IsTrue(nav.MoveToNext("valueQuantity"));
#endif
            elem = nav.Current;
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.Quantity.GetLiteral(), elem.Type[0].Code);

            int getNumberOfChoiceTypes(Type resource, string choiceProperty)
            {
                var mapping = ModelInfo.ModelInspector.FindClassMapping(resource);
                var prop = mapping?.FindMappedElementByChoiceName(choiceProperty) as IElementDefinitionSummary;
                return prop?.Type.Length ?? 0;
            }
        }

        // [WMR 20190819] Verify behavior
        // https://chat.fhir.org/#narrow/stream/179177-conformance/topic/Validator.20error.20for.20modified.20binding.20strength
        [TestMethod]
        public async Tasks.Task TestBindingStrengthConstraint()
        {
            StructureDefinition SpecimenProfile = new StructureDefinition()
            {
                Type = FHIRAllTypes.Specimen.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Specimen),
                Name = nameof(SpecimenProfile),
                Url = "http://example.org/fhir/StructureDefinition/SpecimenProfile",
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        // Renamed element w/o any constraints implies type constraint
                        new ElementDefinition("Specimen.collection.fastingStatus[x]")
                        {
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.CodeableConcept.GetLiteral()
                                }
                            },
                            Binding = new ElementDefinition.ElementDefinitionBindingComponent()
                            {
                                Strength = BindingStrength.Required
                            }
                        },
                    }
                }
            };

            (_, var expanded) = await generateSnapshotAndCompare(SpecimenProfile);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            //dumpElements(expanded.Snapshot.Element);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            //Assert.IsTrue(nav.JumpToFirst("Specimen.collection.fastingStatus[x]");
            Assert.IsTrue(nav.MoveToFirstChild());
            Assert.IsTrue(nav.MoveToChild("collection"));
            Assert.IsTrue(nav.MoveToChild("fastingStatus[x]"));
            var elem = nav.Current;
            Assert.IsNotNull(elem.Binding);
            Assert.AreEqual(BindingStrength.Required, elem.Binding.Strength);
        }

        // [WMR 20190822] R4
        // Verify SnapGen always generates type slicing entry, even if omitted from the diff
        [TestMethod]
        public async Tasks.Task TestTypeSliceGeneratesSliceEntry()
        {
            StructureDefinition SimpleTypeSliceObservationProfile = new StructureDefinition()
            {
                Type = FHIRAllTypes.Observation.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Observation),
                Name = nameof(SimpleTypeSliceObservationProfile),
                Url = "http://example.org/fhir/StructureDefinition/SimpleTypeSliceObservation",
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Observation.valueInteger") { MinValue = new Integer(1) }
                    }
                }
            };

            (_, var expanded) = await generateSnapshotAndCompare(SimpleTypeSliceObservationProfile);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            //dumpElements(expanded.Snapshot.Element);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());
            // Verify that the snapshot contains type slice entry
            Assert.IsTrue(nav.MoveToChild("value[x]"));

            // Verify that the SnapshotGenerator added a default Slicing component
            Assert.IsNotNull(nav.Current.Slicing);
            Assert.AreEqual(1, nav.Current.Slicing.Discriminator.Count);
            Assert.IsTrue(ElementDefinition.DiscriminatorComponent.ForTypeSlice().IsExactly(nav.Current.Slicing.Discriminator[0]));
            //Assert.AreEqual(ElementDefinition.DiscriminatorType.Type, nav.Current.Slicing.Discriminator[0].Type);
            //Assert.AreEqual(ElementDefinition.DiscriminatorComponent.TypeDiscriminatorPath, nav.Current.Slicing.Discriminator[0].Path);

            Assert.IsTrue(nav.MoveToNext());
#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.AreEqual("value[x]", nav.PathName);
#else
            Assert.AreEqual("valueInteger", nav.PathName);
#endif
            Assert.AreEqual("valueInteger", nav.Current.SliceName);
            Assert.IsTrue(nav.Current.MinValue is Integer i && i.Value == 1);
        }

        // [WMR 20190826] Verify correct handling of implicit type slicing through element renaming

        private static readonly StructureDefinition ExtensionWithImplicitTypeSlice = new StructureDefinition()
        {
            Type = FHIRAllTypes.Extension.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Extension),
            Name = nameof(ExtensionWithImplicitTypeSlice),
            Url = "http://example.org/fhir/StructureDefinition/" + nameof(ExtensionWithImplicitTypeSlice),
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.ComplexType,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                {
                    new ElementDefinition("Extension.value[x]")
                    {
                        Type = new List<ElementDefinition.TypeRefComponent>()
                        {
                            new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.String.GetLiteral() }
                        },

#if GENERATE_MISSING_TYPE_SLICE_NAMES && false
                        // Optional slicing component, to indicate slice entry element
                        // Not required by the SnapshotGenerator; may be omitted
                        Slicing = new ElementDefinition.SlicingComponent()
                        {
                            Discriminator = { ElementDefinition.DiscriminatorComponent.ForTypeSlice() }
                        }
#endif

                    },
                    // Renamed element implies type slice
                    new ElementDefinition("Extension.valueString") { Short = "TEST" }
                }
            }
        };

        [TestMethod]
        public async Tasks.Task TestExtensionWithImplicitTypeSlice()
        {
            _generator = new SnapshotGenerator(_testResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(ExtensionWithImplicitTypeSlice);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            dumpElements(expanded.Snapshot.Element, nameof(ExtensionWithImplicitTypeSlice));

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());
            Assert.IsTrue(nav.MoveToChild("value[x]"));
            var elem = nav.Current;
            Assert.IsNotNull(elem.Slicing);
            Assert.IsNotNull(elem.Slicing.Discriminator);
            Assert.AreEqual(1, elem.Slicing.Discriminator.Count);
            Assert.IsTrue(ElementDefinition.DiscriminatorComponent.ForTypeSlice().IsExactly(elem.Slicing.Discriminator[0]));
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.String.GetLiteral(), elem.Type[0].Code);

#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.MoveToNextSlice("valueString"));
#else
            Assert.IsTrue(nav.MoveToNext("valueString"));
#endif
            elem = nav.Current;
            Assert.AreEqual("valueString", elem.SliceName);
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.String.GetLiteral(), elem.Type[0].Code);
            Assert.AreEqual("TEST", elem.Short);

            // Verify end of slice
            Assert.IsFalse(nav.MoveToNext("value[x]"));
            Assert.IsFalse(nav.MoveToNext("valueString"));
        }

        private StructureDefinition PatientWithExtensionWithImplicitTypeSlice = new StructureDefinition()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
            Name = nameof(PatientWithExtensionWithImplicitTypeSlice),
            Url = "http://example.org/fhir/StructureDefinition/" + nameof(PatientWithExtensionWithImplicitTypeSlice),
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.Resource,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Patient.extension")
                        {
                            SliceName = "hairColor",
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.Extension.GetLiteral(),
                                    Profile = new string[] { ExtensionWithImplicitTypeSlice.Url }
                                }
                            }
                        },
                        // Constrain extension child element to force expansion
                        // Constraint on renamed element, similar to the target extension definition
                        new ElementDefinition("Patient.extension.valueString")
                        {
                            Min = 1
                        }
                    }
            }
        };

        [TestMethod]
        public async Tasks.Task TestPatientWithExtensionWithImplicitTypeSlice()
        {
            var resolver = new InMemoryResourceResolver(ExtensionWithImplicitTypeSlice);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(PatientWithExtensionWithImplicitTypeSlice);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            dumpElements(expanded.Snapshot.Element, nameof(PatientWithExtensionWithImplicitTypeSlice));

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());

            // Verify extension slice entry
            Assert.IsTrue(nav.MoveToChild("extension"));
            var elem = nav.Current;
            Assert.IsNotNull(elem.Slicing);
            Assert.IsNotNull(elem.Slicing.Discriminator);
            Assert.AreEqual(1, elem.Slicing.Discriminator.Count);
            Assert.IsTrue(ElementDefinition.DiscriminatorComponent.ForExtensionSlice().IsExactly(elem.Slicing.Discriminator[0]));

            // Verify named extension slice
            Assert.IsTrue(nav.MoveToNext("extension"));
            elem = nav.Current;
            Assert.AreEqual("hairColor", elem.SliceName);
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.Extension.GetLiteral(), elem.Type[0].Code);
            var profile = elem.Type[0].Profile.FirstOrDefault();
            Assert.IsNotNull(profile);
            Assert.AreEqual(ExtensionWithImplicitTypeSlice.Url, profile);

            // Verify type slice entry
            Assert.IsTrue(nav.MoveToChild("value[x]"));
            elem = nav.Current;
            Assert.IsNotNull(elem.Slicing);
            Assert.IsNotNull(elem.Slicing.Discriminator);
            Assert.AreEqual(1, elem.Slicing.Discriminator.Count);
            Assert.IsTrue(ElementDefinition.DiscriminatorComponent.ForTypeSlice().IsExactly(elem.Slicing.Discriminator[0]));
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.String.GetLiteral(), elem.Type[0].Code);

            // Verify named type slice
#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.MoveToNextSlice("valueString"));
#else
            Assert.IsTrue(nav.MoveToNext("valueString"));
#endif
            elem = nav.Current;
            Assert.AreEqual("valueString", elem.SliceName);
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.String.GetLiteral(), elem.Type[0].Code);
            Assert.AreEqual("TEST", elem.Short); // Inherited from extension profile
            Assert.AreEqual(1, elem.Min);        // Inline profile constraint

            // Verify end of slice
            Assert.IsFalse(nav.MoveToNext("value[x]"));
            Assert.IsFalse(nav.MoveToNext("valueString"));
        }

        private StructureDefinition PatientWithExtensionWithImplicitTypeSliceMixed = new StructureDefinition()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
            Name = nameof(PatientWithExtensionWithImplicitTypeSliceMixed),
            Url = "http://example.org/fhir/StructureDefinition/" + nameof(PatientWithExtensionWithImplicitTypeSliceMixed),
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.Resource,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Patient.extension")
                        {
                            SliceName = "hairColor",
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.Extension.GetLiteral(),
                                    Profile = new string[] { ExtensionWithImplicitTypeSlice.Url }
                                }
                            }
                        },
                        // Constrain extension child element to force expansion
                        // Constraint on explicit type slice, should match renamed element "valueString"
                        new ElementDefinition("Patient.extension.value[x]")
                        {
                            SliceName = "valueString",
                            Min = 1
                        }
                    }
            }
        };

        // Verify merging of type slicing constraints using mixed notation
        // Referenced extension definition specifies implicit type slice (renaming)
        // Inline profile constraint specifies verbose type slice (no renaming)

        [TestMethod]
        public async Tasks.Task TestPatientWithExtensionWithImplicitTypeSliceMixed()
        {
            var resolver = new InMemoryResourceResolver(ExtensionWithImplicitTypeSlice);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(PatientWithExtensionWithImplicitTypeSliceMixed);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            dumpElements(expanded.Snapshot.Element, nameof(PatientWithExtensionWithImplicitTypeSliceMixed));

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());

            // Verify extension slice entry
            Assert.IsTrue(nav.MoveToChild("extension"));
            var elem = nav.Current;
            Assert.IsNull(elem.SliceName);
            Assert.IsNotNull(elem.Slicing);
            Assert.IsNotNull(elem.Slicing.Discriminator);
            Assert.AreEqual(1, elem.Slicing.Discriminator.Count);
            Assert.IsTrue(ElementDefinition.DiscriminatorComponent.ForExtensionSlice().IsExactly(elem.Slicing.Discriminator[0]));

            // Verify named extension slice
            Assert.IsTrue(nav.MoveToNext("extension"));
            elem = nav.Current;
            Assert.AreEqual("hairColor", elem.SliceName);
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.Extension.GetLiteral(), elem.Type[0].Code);
            var profile = elem.Type[0].Profile.FirstOrDefault();
            Assert.IsNotNull(profile);
            Assert.AreEqual(ExtensionWithImplicitTypeSlice.Url, profile);

            // Verify type slice entry
            Assert.IsTrue(nav.MoveToChild("value[x]"));
            elem = nav.Current;
            Assert.IsNull(elem.SliceName);
            Assert.IsNotNull(elem.Slicing);
            Assert.IsNotNull(elem.Slicing.Discriminator);
            Assert.AreEqual(1, elem.Slicing.Discriminator.Count);
            Assert.IsTrue(ElementDefinition.DiscriminatorComponent.ForTypeSlice().IsExactly(elem.Slicing.Discriminator[0]));
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.String.GetLiteral(), elem.Type[0].Code);

            // Verify named type slice
#if NORMALIZE_RENAMED_TYPESLICE
            // [WMR 20190828] R4: Normalize renamed type slices in snapshot
            Assert.IsTrue(nav.MoveToNextSlice("valueString"));
#else
            Assert.IsTrue(nav.MoveToNext("valueString"));
#endif
            elem = nav.Current;
            Assert.AreEqual("valueString", elem.SliceName);
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.String.GetLiteral(), elem.Type[0].Code);
            Assert.AreEqual("TEST", elem.Short); // Inherited from extension profile
            Assert.AreEqual(1, elem.Min);        // Inline profile constraint

            // Verify end of slice
            Assert.IsFalse(nav.MoveToNext("value[x]"));
            Assert.IsFalse(nav.MoveToNext("valueString"));
        }


        // [WMR 20190826] Verify correct handling of verbose type slicing w/o renaming

        private static readonly StructureDefinition ExtensionWithVerboseTypeSlice = new StructureDefinition()
        {
            Type = FHIRAllTypes.Extension.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Extension),
            Name = nameof(ExtensionWithVerboseTypeSlice),
            Url = "http://example.org/fhir/StructureDefinition/" + nameof(ExtensionWithVerboseTypeSlice),
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.ComplexType,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Extension.value[x]")
                        {
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.String.GetLiteral() }
                            },
                            // Explicit Slicing entry
                            Slicing = new ElementDefinition.SlicingComponent()
                            {
                                Discriminator = new List<ElementDefinition.DiscriminatorComponent>()
                                {
                                    ElementDefinition.DiscriminatorComponent.ForTypeSlice()
                                }
                            }
                        },
                        // Named type slice w/o renaming
                        new ElementDefinition("Extension.value[x]")
                        {
                            SliceName = "valueString",
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.String.GetLiteral() }
                            },
                            Short="TEST"
                        }
                    }
            }
        };

        [TestMethod]
        public async Tasks.Task TestExtensionWithVerboseTypeSlice()
        {
            _generator = new SnapshotGenerator(_testResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(ExtensionWithVerboseTypeSlice);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            dumpElements(expanded.Snapshot.Element, nameof(ExtensionWithVerboseTypeSlice));

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());
            Assert.IsTrue(nav.MoveToChild("value[x]"));
            var elem = nav.Current;
            Assert.IsNull(elem.SliceName);
            Assert.IsNotNull(elem.Slicing);
            Assert.IsNotNull(elem.Slicing.Discriminator);
            Assert.AreEqual(1, elem.Slicing.Discriminator.Count);
            Assert.IsTrue(ElementDefinition.DiscriminatorComponent.ForTypeSlice().IsExactly(elem.Slicing.Discriminator[0]));
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.String.GetLiteral(), elem.Type[0].Code);

            Assert.IsTrue(nav.MoveToNext("value[x]"));
            elem = nav.Current;
            Assert.AreEqual("valueString", elem.SliceName);
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.String.GetLiteral(), elem.Type[0].Code);
            Assert.AreEqual("TEST", elem.Short);

            // Verify end of slice
            Assert.IsFalse(nav.MoveToNext("value[x]"));
            Assert.IsFalse(nav.MoveToNext("valueString"));
        }

        private StructureDefinition PatientWithExtensionWithVerboseTypeSlice = new StructureDefinition()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
            Name = nameof(PatientWithExtensionWithVerboseTypeSlice),
            Url = "http://example.org/fhir/StructureDefinition/" + nameof(PatientWithExtensionWithVerboseTypeSlice),
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.Resource,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                    {
                        // Omitted: implicit slice on Patient.extension
                        new ElementDefinition("Patient.extension")
                        {
                            SliceName = "hairColor",
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.Extension.GetLiteral(),
                                    Profile = new string[] { ExtensionWithVerboseTypeSlice.Url }
                                }
                            }
                        },
                        // Constrain extension child element to force expansion
                        new ElementDefinition("Patient.extension.value[x]")
                        {
                            SliceName = "valueString",
                            Min = 1
                        }
                    }
            }
        };

        [TestMethod]
        public async Tasks.Task TestPatientWithExtensionWithVerboseTypeSlice()
        {
            var resolver = new InMemoryResourceResolver(ExtensionWithVerboseTypeSlice);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(PatientWithExtensionWithVerboseTypeSlice);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            dumpElements(expanded.Snapshot.Element, nameof(PatientWithExtensionWithVerboseTypeSlice));

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());

            // Verify extension slice entry
            Assert.IsTrue(nav.MoveToChild("extension"));
            var elem = nav.Current;
            Assert.IsNull(elem.SliceName);
            Assert.IsNotNull(elem.Slicing);
            Assert.IsNotNull(elem.Slicing.Discriminator);
            Assert.AreEqual(1, elem.Slicing.Discriminator.Count);
            Assert.IsTrue(ElementDefinition.DiscriminatorComponent.ForExtensionSlice().IsExactly(elem.Slicing.Discriminator[0]));

            // Verify named extension slice
            Assert.IsTrue(nav.MoveToNext("extension"));
            elem = nav.Current;
            Assert.AreEqual("hairColor", elem.SliceName);
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.Extension.GetLiteral(), elem.Type[0].Code);
            var profile = elem.Type[0].Profile.FirstOrDefault();
            Assert.IsNotNull(profile);
            Assert.AreEqual(ExtensionWithVerboseTypeSlice.Url, profile);

            // Verify type slice entry
            Assert.IsTrue(nav.MoveToChild("value[x]"));
            elem = nav.Current;
            Assert.IsNull(elem.SliceName);
            Assert.IsNotNull(elem.Slicing);
            Assert.IsNotNull(elem.Slicing.Discriminator);
            Assert.AreEqual(1, elem.Slicing.Discriminator.Count);
            Assert.IsTrue(ElementDefinition.DiscriminatorComponent.ForTypeSlice().IsExactly(elem.Slicing.Discriminator[0]));
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.String.GetLiteral(), elem.Type[0].Code);

            // Verify named type slice
            Assert.IsTrue(nav.MoveToNext("value[x]"));
            elem = nav.Current;
            Assert.AreEqual("valueString", elem.SliceName);
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.String.GetLiteral(), elem.Type[0].Code);
            Assert.AreEqual("TEST", elem.Short); // Inherited from extension profile
            Assert.AreEqual(1, elem.Min);        // Inline profile constraint

            // Verify end of slice
            Assert.IsFalse(nav.MoveToNext("value[x]"));
            Assert.IsFalse(nav.MoveToNext("valueString"));
        }

        private StructureDefinition PatientWithExtensionWithVerboseTypeSliceMixed = new StructureDefinition()
        {
            Type = FHIRAllTypes.Patient.GetLiteral(),
            BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
            Name = nameof(PatientWithExtensionWithVerboseTypeSliceMixed),
            Url = "http://example.org/fhir/StructureDefinition/" + nameof(PatientWithExtensionWithVerboseTypeSliceMixed),
            Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            Kind = StructureDefinition.StructureDefinitionKind.Resource,
            Differential = new StructureDefinition.DifferentialComponent()
            {
                Element = new List<ElementDefinition>()
                    {
                        // Omitted: implicit slice on Patient.extension
                        new ElementDefinition("Patient.extension")
                        {
                            SliceName = "hairColor",
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.Extension.GetLiteral(),
                                    Profile = new string[] { ExtensionWithVerboseTypeSlice.Url }
                                }
                            }
                        },
                        // Constrain extension child element to force expansion
                        // Constraint on renamed element, unlike target extension definition (with verbose type slice)
                        new ElementDefinition("Patient.extension.valueString")
                        {
                            Min = 1
                        }
                    }
            }
        };

        [TestMethod]
        public async Tasks.Task TestPatientWithExtensionWithVerboseTypeSliceMixed()
        {
            var resolver = new InMemoryResourceResolver(ExtensionWithVerboseTypeSlice);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            (_, var expanded) = await generateSnapshotAndCompare(PatientWithExtensionWithVerboseTypeSliceMixed);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            dumpElements(expanded.Snapshot.Element, nameof(PatientWithExtensionWithVerboseTypeSliceMixed));

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());

            // Verify extension slice entry
            Assert.IsTrue(nav.MoveToChild("extension"));
            var elem = nav.Current;
            Assert.IsNull(elem.SliceName);
            Assert.IsNotNull(elem.Slicing);
            Assert.IsNotNull(elem.Slicing.Discriminator);
            Assert.AreEqual(1, elem.Slicing.Discriminator.Count);
            Assert.IsTrue(ElementDefinition.DiscriminatorComponent.ForExtensionSlice().IsExactly(elem.Slicing.Discriminator[0]));

            // Verify named extension slice
            Assert.IsTrue(nav.MoveToNext("extension"));
            elem = nav.Current;
            Assert.AreEqual("hairColor", elem.SliceName);
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.Extension.GetLiteral(), elem.Type[0].Code);
            var profile = elem.Type[0].Profile.FirstOrDefault();
            Assert.IsNotNull(profile);
            Assert.AreEqual(ExtensionWithVerboseTypeSlice.Url, profile);

            // Verify type slice entry
            Assert.IsTrue(nav.MoveToChild("value[x]"));
            elem = nav.Current;
            Assert.IsNull(elem.SliceName);
            Assert.IsNotNull(elem.Slicing);
            Assert.IsNotNull(elem.Slicing.Discriminator);
            Assert.AreEqual(1, elem.Slicing.Discriminator.Count);
            Assert.IsTrue(ElementDefinition.DiscriminatorComponent.ForTypeSlice().IsExactly(elem.Slicing.Discriminator[0]));
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.String.GetLiteral(), elem.Type[0].Code);

            // Verify named type slice
            //Assert.IsTrue(nav.MoveToNext("valueString")); // NOT normalized...
            Assert.IsTrue(nav.MoveToNext("value[x]"));      // Normalized
            elem = nav.Current;
            Assert.AreEqual("valueString", elem.SliceName);
            Assert.IsNotNull(elem.Type);
            Assert.AreEqual(1, elem.Type.Count);
            Assert.AreEqual(FHIRAllTypes.String.GetLiteral(), elem.Type[0].Code);
            Assert.AreEqual("TEST", elem.Short); // Inherited from extension profile

            // [WMR 20190826] FAILS!
            // Problem: invalid matching
            //
            // Matches for children of 'PatientWithExtensionWithVerboseTypeSliceMixed' : Patient.extension 'Extension'
            // B:Patient.extension.id <-- Merge --> D:Patient.extension.id
            // B:Patient.extension.extension <-- Slice --> D:Patient.extension.extension -- SliceBase: Patient.extension.extension
            // B:Patient.extension.url <-- Merge --> D:Patient.extension.url
            // B:Patient.extension.value[x] <-- Slice --> D:Patient.extension.value[x] -- SliceBase: Patient.extension.value[x]
            // [WRONG!] B:Patient.extension.value[x] 'valueString' <-- Merge --> D:Patient.extension.value[x] 'valueString' -- SliceBase: Patient.extension.value[x] 'valueString'
            // Matches for children of 'PatientWithExtensionWithVerboseTypeSliceMixed' : Patient.extension 'hairColor'
            // B:Patient.extension.value[x] <-- Merge --> D:Patient.extension.valueString -- SliceBase: Patient.extension.value[x]
            //

            Assert.AreEqual(1, elem.Min);        // Inline profile constraint

            // Verify end of slice
            Assert.IsFalse(nav.MoveToNext("value[x]"));
            Assert.IsFalse(nav.MoveToNext("valueString"));
        }

#if false

        // [WMR 20190823] Verify profile with reference to extension with type slice on Extension.value[x]
        [TestMethod]
        public void TestProfileExtensionValue()
        {
            StructureDefinition SimplePatientExtension = new StructureDefinition()
            {
                Type = FHIRAllTypes.Extension.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Extension),
                Name = nameof(SimplePatientExtension),
                Url = "http://example.org/fhir/StructureDefinition/" + nameof(SimplePatientExtension),
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.ComplexType,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Extension.value[x]")
                        {
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.String.GetLiteral() }
                            }
                            // Implicit type Slicing entry
                        },
                        // Renaming implies type constraint
                        new ElementDefinition("Extension.valueString")
                        {
                        }
                    }
                }
            };

            StructureDefinition PatientWithExtension = new StructureDefinition()
            {
                Type = FHIRAllTypes.Patient.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
                Name = nameof(PatientWithExtension),
                Url = "http://example.org/fhir/StructureDefinition/" + nameof(PatientWithExtension),
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Patient.extension")
                        {
                            SliceName = "hairColor",
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.Extension.GetLiteral(),
                                    Profile = new string[] { SimplePatientExtension.Url }
                                }
                            }
                        },


                        // [WMR 20190823] TODO - Gracefully handle both notations
                        // - "valueString" => snapshot is correct
                        // - "value[x]:valueString" => snapshot is incorrect, includes valueString + value[x]:valueString (WRONG!)
                        // Create separate unit tests for both notations, to prevent regressions


                        // Constrain extension child element to force expansion
                        //new ElementDefinition("Patient.extension.valueString")
                        new ElementDefinition("Patient.extension.value[x]")
                        {
                            SliceName = "valueString",
                            //Comment = new Markdown("TEST")
                            Short="TEST"
                        }
                    }
                }
            };

            var resolver = new InMemoryResourceResolver(PatientWithExtension, SimplePatientExtension);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            generateSnapshotAndCompare(PatientWithExtension, out StructureDefinition expanded);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            dumpElements(expanded.Snapshot.Element);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());
            Assert.IsTrue(nav.MoveToChild("extension"));

            // Verify that the SnapshotGenerator added a default Slicing component for extension
            Assert.IsNotNull(nav.Current.Slicing);
            Assert.AreEqual(1, nav.Current.Slicing.Discriminator.Count);
            Assert.IsTrue(ElementDefinition.DiscriminatorComponent.ForExtensionSlice().IsExactly(nav.Current.Slicing.Discriminator[0]));

            Assert.IsTrue(nav.MoveToNext("extension"));
            Assert.AreEqual("hairColor", nav.Current.SliceName);

            // Verify type slice entry for value[x] element
            Assert.IsTrue(nav.MoveToChild("value[x]"));
            Assert.AreEqual(1, nav.Current.Slicing.Discriminator.Count);
            Assert.IsTrue(ElementDefinition.DiscriminatorComponent.ForTypeSlice().IsExactly(nav.Current.Slicing.Discriminator[0]));
            Assert.IsNotNull(nav.Current.Type);
            Assert.AreEqual(1, nav.Current.Type.Count);
            Assert.AreEqual(FHIRAllTypes.String.GetLiteral(), nav.Current.Type[0].Code);

            Assert.IsTrue(nav.MoveToNext());
            Assert.AreEqual("value[x]", nav.PathName);
            //Assert.AreEqual("valueString", nav.PathName); // Hmmm... snapshot should be normalized to value[x]
            Assert.AreEqual("valueString", nav.Current.SliceName);

            //var comment = nav.Current.Comment as Markdown;
            //Assert.IsNotNull(comment);
            //Assert.AreEqual("TEST", comment.Value);
            Assert.AreEqual("TEST", nav.Current.Short);

            Assert.IsNotNull(nav.Current.Type);
            Assert.AreEqual(1, nav.Current.Type.Count);
            Assert.AreEqual(FHIRAllTypes.String.GetLiteral(), nav.Current.Type[0].Code);

            // Verify that this is the last slice
            Assert.IsFalse(nav.MoveToNext());
        }
#endif

        // [WMR 20190910] Issue #1098 - Normalize element paths of type slices in referenced extensions
        [TestMethod]
        public async Tasks.Task TestNormalizeTypeSliceInExtension()
        {
            const string url = @"http://hl7.org/fhir/StructureDefinition/data-absent-reason-fortest";

            var sd = await _testResolver.FindStructureDefinitionAsync(url);
            Assert.IsNotNull(sd);

            var nav = ElementDefinitionNavigator.ForDifferential(sd);
            Assert.IsTrue(nav.MoveToFirstChild());
            // Verify that differential specifies renamed type slice
            Assert.IsTrue(nav.MoveToChild("valueCode"));

            (_, var expanded) = await generateSnapshotAndCompare(sd);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            dumpElements(expanded.Snapshot.Element, expanded.Title);

            nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());
            // Verify type slice in snapshot has normalized element path "value[x]:valueCode"
            Assert.IsFalse(nav.MoveToChild("valueCode"));
        }

        // #1116 Extension.url requires fixedUri, not fixedString
        // https://chat.fhir.org/#narrow/stream/179177-conformance/topic/Extension.2Eurl.20-.20fixedString.20or.20fixedUri.3F

        [TestMethod]
        public async Tasks.Task TestExtensionUrlFixedValueSimple()
        {
            StructureDefinition SimpleTestExtension = new StructureDefinition()
            {
                Type = FHIRAllTypes.Extension.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Extension),
                Name = nameof(SimpleTestExtension),
                Url = "http://example.org/fhir/StructureDefinition/" + nameof(SimpleTestExtension),
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.ComplexType,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        // Extension.url omitted, should be generated by SnapGen
                        new ElementDefinition("Extension.value[x]")
                        {
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.String.GetLiteral() }
                            }
                        }
                    }
                }
            };

            (_, var expanded) = await generateSnapshotAndCompare(SimpleTestExtension);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());
            AssertExtensionUrlChildElement(nav, SimpleTestExtension.Url);
        }

        [TestMethod]
        public async Tasks.Task TestExtensionUrlFixedValueComplex()
        {
            StructureDefinition ComplexTestExtension = new()
            {
                Type = FHIRAllTypes.Extension.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Extension),
                Name = nameof(ComplexTestExtension),
                Url = "http://example.org/fhir/StructureDefinition/" + nameof(ComplexTestExtension),
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.ComplexType,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        // Extension.url omitted, should be generated by SnapGen
                        new ElementDefinition("Extension.extension")
                        {
                            SliceName = "X"
                        },
                        // Extension.extension.url omitted, should be generated by SnapGen
                        new ElementDefinition("Extension.extension.value[x]")
                        {
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.Decimal.GetLiteral() }
                            }
                        },
                        new ElementDefinition("Extension.extension")
                        {
                            SliceName = "Y"
                        },
                        // Extension.extension.url omitted, should be generated by SnapGen
                        new ElementDefinition("Extension.extension.value[x]")
                        {
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.Decimal.GetLiteral() }
                            }
                        }
                    }
                }
            };

            (_, var expanded) = await generateSnapshotAndCompare(ComplexTestExtension);
            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.MoveToFirstChild());

            Assert.IsTrue(nav.MoveToChild("extension"));
            Assert.IsNotNull(nav.Current.Slicing);

            Assert.IsTrue(nav.MoveToNextSlice());
            Assert.AreEqual("X", nav.Current.SliceName);

            AssertExtensionUrlChildElement(nav, "X");

            Assert.IsTrue(nav.MoveToNextSlice());
            Assert.AreEqual("Y", nav.Current.SliceName);

            AssertExtensionUrlChildElement(nav, "Y");

            Assert.IsTrue(nav.MoveToNext("url"));
            AssertExtensionUrlElement(nav, ComplexTestExtension.Url);
        }

        // TODO: Derived extension profile
        // fixedUri should inherit values from base profile
        // i.e. do NOT replace with canonical url of derived profile...!

        private static void AssertExtensionUrlChildElement(ElementDefinitionNavigator nav, string url)
        {
            var bm = nav.Bookmark();
            Assert.IsTrue(nav.MoveToChild("url"));
            AssertExtensionUrlElement(nav, url);
            nav.ReturnToBookmark(bm);
        }

        private static void AssertExtensionUrlElement(ElementDefinitionNavigator nav, string url)
        {
            Assert.IsTrue(nav.Path.ToLowerInvariant().EndsWith("extension.url"));
            var fixedValue = nav.Current.Fixed;
            Assert.IsNotNull(fixedValue);
            Assert.IsInstanceOfType(fixedValue, typeof(IValue<string>));
            Assert.IsInstanceOfType(fixedValue, typeof(FhirUri));
            var fixedUrl = (IValue<string>)fixedValue;
            Assert.AreEqual(url, fixedUrl.Value);
        }

        [TestMethod]
        public async Tasks.Task TestElementWithoutPath()
        {
            var sd = new StructureDefinition()
            {
                Type = FHIRAllTypes.Patient.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
                Name = "MyInvalidPatient",
                Url = "http://example.org/fhir/StructureDefinition/InvalidPatient",
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition()
                        {
                            // No path...
                            Min = 1
                        },
                    }
                }
            };

            async Tasks.Task generate()
            {
                await generateSnapshotAndCompare(sd);
            }

            // [WMR 20190910] Expecting exception from DifferentialTreeConstructor
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(generate);
        }

        // [WMR 20190902] #1090 SnapshotGenerator should support logical models
        // STU3: Serialize logical model to StructureDefinition.snapshot, .differential is always empty
        // R4: Serialize logical model to StructureDefinition.differential, generate .snapshot
        [TestMethod]
        public async Tasks.Task TestLogicalModel()
        {
            const string rootPath = "MyModel";
            var SimpleLogicalModel = new StructureDefinition()
            {
                Url = "http://example.org/fhir/StructureDefinition/SimpleLogicalModel",
                Name = "SimpleLogicalModel",
                Kind = StructureDefinition.StructureDefinitionKind.Logical,
                // Last segment equals root element name
                Type = "http://example.org/fhir/StructureDefinition/" + rootPath,
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Element),
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition(rootPath)
                        {
                            //Min = 0,
                            //Max = "*",
                            //Type = new List<ElementDefinition.TypeRefComponent>()
                            //{
                            //    new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.Element.GetLiteral() }
                            //}
                        },
                        new ElementDefinition(rootPath + ".target")
                        {
                            Min = 0,
                            Max = "1",
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.Reference.GetLiteral(),
                                    TargetProfile = new string[] { ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Person) }
                                }
                            }
                        },
                        new ElementDefinition(rootPath + ".value[x]")
                        {
                            Min = 0,
                            Max = "*",
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.String.GetLiteral(),
                                },
                                new ElementDefinition.TypeRefComponent()
                                {
                                    Code = FHIRAllTypes.Boolean.GetLiteral(),
                                }
                            }
                        }
                    }
                }
            };

            (_, var expanded) = await generateSnapshotAndCompare(SimpleLogicalModel);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            void assertElementBase(ElementDefinition elem)
            {
                Assert.IsNotNull(elem);
                Assert.IsNotNull(elem.Base);
                Assert.IsNotNull(elem.Base.Path);
                Assert.IsNotNull(elem.Base.MinElement);
                Assert.IsNotNull(elem.Base.MaxElement);
            }

            // Verify sdf-8b: "All snapshot elements must have a base definition"
            expanded.Snapshot.Element.ForEach(e => assertElementBase(e));
        }


        // #1123 SnapshotGenerator - ElementDefinition.base is empty for children of contentreference

        [TestMethod]
        public async Tasks.Task TestElementDefinitionBase_ContentReference()
        {
            // Verify that the snapshot generator correctly expands elements with a targetProfile (on ResourceReference itself)
            var ProvenanceProfile = new StructureDefinition()
            {
                Type = FHIRAllTypes.Provenance.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Provenance),
                Name = "ProvenanceProfile",
                Url = "http://example.org/fhir/StructureDefinition/ProvenanceProfile",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Provenance.entity.agent.who[x]")
                        {
                            Comment = new Markdown("CustomReference")
                        },
                    }
                }
            };

            (_, var expanded) = await generateSnapshotAndCompare(ProvenanceProfile);

            dumpOutcome(_generator.Outcome);
            dumpBasePaths(expanded);

            Assert.IsNotNull(expanded);
            Assert.IsTrue(expanded.HasSnapshot);

            var nav = ElementDefinitionNavigator.ForSnapshot(expanded);
            Assert.IsTrue(nav.JumpToFirst("Provenance.agent"));
            var refNav = new ElementDefinitionNavigator(nav);
            Assert.IsTrue(refNav.JumpToFirst("Provenance.entity.agent"));
            Assert.IsNotNull(refNav.Current.Base);
            Assert.AreEqual(refNav.Current.Path, refNav.Current.Base.Path);

            // Verify that content reference children inherit .base.path from content reference target
            var startPos = nav.OrdinalPosition.Value;
            var refRootPath = refNav.Path;
            for (int i = startPos + 1; i < nav.Elements.Count; i++)
            {
                var tgtElem = nav.Elements[i];
                var refElem = refNav.Elements[i];
                if (i > startPos && !ElementDefinitionNavigator.IsChildPath(refRootPath, refElem.Path))
                {
                    break;
                }
                Assert.IsNotNull(refElem.Base);
                Assert.AreEqual(tgtElem.Base.Path, refElem.Base.Path);
            }
        }

        // #1108/#1303 - incorrectly copies the 0..* root cardinality of a referenced datatype profile
        // over unto an element that has base cardinality 0..1
        [TestMethod]
        public async Tasks.Task ShouldRespectMaxCardinalityFromBase()
        {
            var cr = new CachedResolver(
                new SnapshotSource(
                    new MultiResolver(
                        new CachedResolver(new TestProfileArtifactSource()),
                        new ZipSource("specification.zip"))));

            var range = await cr.FindStructureDefinitionAsync("http://validationtest.org/fhir/StructureDefinition/RangeWithLowAsAQuantityWithUnlimitedRootCardinality");
            var lowElement = range.Snapshot.Element.Single(e => e.Path == "Range.low");
            Assert.AreEqual(1, lowElement.Min);
            Assert.AreEqual("1", lowElement.Max);   // the referred profile has "*", but the base has "1". It should become "1"
        }

        [TestMethod]
        public async Tasks.Task NewSlicetoDerivedProfile()
        {
            var resolver = new CachedResolver(
                new SnapshotSource(
                    new MultiResolver(
                        new CachedResolver(
                            new TestProfileArtifactSource()),
                            _standardFhirSource)));

            var patient = await resolver.FindStructureDefinitionAsync("http://validationtest.org/fhir/StructureDefinition/mi-patient");
            patient.Should().NotBeNull("A snapshot must be created");

            var newSliceSystem = patient.Snapshot.Element.FirstOrDefault(e => e.ElementId == "Patient.identifier:newSlice.system");
            newSliceSystem.Should().NotBeNull("The new slice 'newSlice' should be present in the snapshot");
            newSliceSystem.Fixed.Should().BeNull("No constraint elements from the base slice (BSN) should be present");
        }

        [TestMethod]
        public async Tasks.Task AddingSliceInClosedSlicing()
        {
            var testProfiles = new TestProfileArtifactSource();

            var resolver = new CachedResolver(
                new SnapshotSource(
                    new MultiResolver(
                        new CachedResolver(
                            new TestProfileArtifactSource()),
                            _standardFhirSource)));

            var observation = await resolver.FindStructureDefinitionAsync("http://validationtest.org/fhir/StructureDefinition/ObservationSlicingCodeableConcept");

            var openingSlice = observation.Snapshot.Element.FirstOrDefault(e => e.ElementId == "Observation.value[x]");
            openingSlice.Should().NotBeNull("The opening slice should be present in the snapshot");
            openingSlice.Type.Should().OnlyContain(t => t.Code == "CodeableConcept");

            Func<Tasks.Task> act = async () => { await resolver.FindStructureDefinitionAsync("http://validationtest.org/fhir/StructureDefinition/ObservationValueSlicingQuantity"); };
            await act
              .Should().ThrowAsync<InvalidOperationException>()
              .WithMessage("*choice type of diff does not occur in snap*");
        }

        [TestMethod]
        public async Tasks.Task SnapshotSucceedsWithExtendedVariantElementDef()
        {
            var structureDef = new StructureDefinition
            {
                BaseDefinition = "http://hl7.org/fhir/StructureDefinition/Observation",
                Type = "Observation",
                Url = "http://some.canonical",

                Differential = new StructureDefinition.DifferentialComponent
                {
                    Element = new System.Collections.Generic.List<ElementDefinition>{
                    new ElementDefinition
                    {
                        ElementId = "Observation.value[x].extension",
                        Path = "Observation.value[x].extension",
                        Slicing = new ElementDefinition.SlicingComponent
                        {
                            Discriminator = new System.Collections.Generic.List<ElementDefinition.DiscriminatorComponent>
                            {
                                new ElementDefinition.DiscriminatorComponent
                                {
                                    Type = ElementDefinition.DiscriminatorType.Value,
                                    Path = "url"
                                },
                            },
                            Rules = ElementDefinition.SlicingRules.Open
                        }
                    },
                    new ElementDefinition
                    {
                        ElementId = "Observation.value[x].extension:myExtension",
                        Path = "Observation.value[x].extension",
                        SliceName = "myExtension",
                        Type = new System.Collections.Generic.List<ElementDefinition.TypeRefComponent>
                        {
                            new ElementDefinition.TypeRefComponent
                            {
                                Code = "Extension",
                                Profile = new string[]
                                {
                                    "http://example.org/fhir/StructureDefinition/MyExtension"
                                }
                            }
                        }
                    }
                }
                }
            };


            _generator = new SnapshotGenerator(_testResolver, SnapshotGeneratorSettings.CreateDefault());

            await _generator.UpdateAsync(structureDef);

            structureDef.Snapshot.Element.Where(element => element.Path == "Observation.value[x].extension").Should().HaveCount(2, "Elements are in the snapshot");
            structureDef.Snapshot.Element.Where(element => element.Path == "Observation.extension").Should().HaveCount(1, "Only the root extension should be there");
        }

        [TestMethod]
        public async Tasks.Task TestExtensionValueXCommentShouldBeNull()
        {
            const string ElementId = "Extension.value[x]";

            var resolver = new MultiResolver(_standardFhirSource);
            var sd = await resolver.FindStructureDefinitionForCoreTypeAsync(nameof(Extension));

            var element = sd.Snapshot.Element.Single(x => x.ElementId == ElementId);
            element.Comment.Should().BeNull();

            var generator = new SnapshotGenerator(resolver, SnapshotGeneratorSettings.CreateDefault());

            generator.PrepareElement += (sender, e) =>
            {
                if (e.Element.Path == ElementId)
                {
                    Debug.WriteLine($"Element:{ElementId} BaseElement:{e?.BaseElement?.ElementId}");
                }
            };

            // Act
            await generator.UpdateAsync(sd);

            // Assert
            element = sd.Snapshot.Element.Single(x => x.ElementId == ElementId);
            element.Comment.Should().BeNull();
        }

        [TestMethod]
        public async Tasks.Task CheckCardinalityOfProfiledType()
        {
            var resolver = new CachedResolver(new MultiResolver(_standardFhirSource, new TestProfileArtifactSource()));
            var snapshotGenerator = new SnapshotGenerator(resolver, SnapshotGeneratorSettings.CreateDefault());
            var sd = await resolver.ResolveByCanonicalUriAsync("http://hl7.org/fhir/StructureDefinition/Observation") as StructureDefinition;
            var sut = await resolver.ResolveByCanonicalUriAsync("http://validationtest.org/fhir/StructureDefinition/ObservationWithTranslatableCode") as StructureDefinition;

            // Act
            var elements = await snapshotGenerator.GenerateAsync(sut);

            // Assert
            snapshotGenerator.Outcome.Should().BeNull();

            const string codeId = "Observation.code";

            var sdCode = sd.Snapshot.Element.Single(x => x.ElementId == codeId);
            var sutCode = elements.Single(x => x.ElementId == codeId);

            sutCode.Max.Should().Be(sdCode.Max);
            sutCode.Min.Should().Be(sdCode.Min);
        }

        [TestMethod]
        public async Tasks.Task TestAbsoluteContentReferenceGeneration()
        {
            //prepare 
            var generator = new SnapshotGenerator(_standardFhirSource, SnapshotGeneratorSettings.CreateDefault());


            //Test if core resource has relative content references.
            var coreQuestionnaire = await _testResolver.FindStructureDefinitionAsync("http://hl7.org/fhir/StructureDefinition/Questionnaire");
            var coreSnapshot = await generator.GenerateAsync(coreQuestionnaire);
            var item = coreSnapshot.Where(e => e.Path == "Questionnaire.item.item").FirstOrDefault();
            item.ContentReference.Should().Be("#Questionnaire.item");


            //Create profile for testing creation of absolute references.
            var profile = new StructureDefinition
            {
                Url = "http://firely-sdk.org/fhir/StructureDefinition/content-reference-check",
                Status = PublicationStatus.Draft,
                FhirVersion = FHIRVersion.N4_0_1,
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Abstract = false,
                Type = "Questionnaire",
                BaseDefinition = "http://hl7.org/fhir/StructureDefinition/Questionnaire",
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Differential = new StructureDefinition.DifferentialComponent
                {
                    Element = new List<ElementDefinition>
                    {
                        new ElementDefinition
                        {
                            ElementId = "Questionnaire.item",
                            Path = "Questionnaire.item",
                            Slicing = new ElementDefinition.SlicingComponent
                            {
                                Discriminator = new List<ElementDefinition.DiscriminatorComponent>
                                {
                                    new ElementDefinition.DiscriminatorComponent
                                    {
                                        Type = ElementDefinition.DiscriminatorType.Value,
                                        Path = "type"
                                    }
                                },
                                Rules = ElementDefinition.SlicingRules.Open
                            }
                        },
                        new ElementDefinition
                        {
                            ElementId = "Questionnaire.item:booleanItem",
                            Path = "Questionnaire.item",
                            SliceName = "booleanItem",
                            Min = 1
                        },
                        new ElementDefinition
                        {
                            ElementId = "Questionnaire.item:booleanItem.type",
                            Path = "Questionnaire.item.type",
                            Fixed = new Code("boolean")
                        },
                        new ElementDefinition
                        {
                            ElementId = "Questionnaire.item:booleanItem.item.type",
                            Path = "Questionnaire.item.item.type",
                            Fixed = new Code("string")
                        }
                    }
                }
            };

            // test if profiles have absolute content references.
            var profileSnapshot = await generator.GenerateAsync(profile);

            var cref1 = profileSnapshot.Where(e => e.ElementId == "Questionnaire.item:booleanItem.item").FirstOrDefault();
            cref1.ContentReference.Should().Be("http://hl7.org/fhir/StructureDefinition/Questionnaire#Questionnaire.item");

            var cref2 = profileSnapshot.Where(e => e.ElementId == "Questionnaire.item:booleanItem.item.item").FirstOrDefault();
            cref2.ContentReference.Should().Be("http://hl7.org/fhir/StructureDefinition/Questionnaire#Questionnaire.item");
        }

        [DataTestMethod]
        [DataRow("http://validationtest.org/fhir/StructureDefinition/DeceasedPatient", "Patient.deceased[x].extension:range")]
        [DataRow("http://validationtest.org/fhir/StructureDefinition/DeceasedPatientRequiredBoolean", "Patient.deceased[x].extension:range")]
        public async Tasks.Task ContinueMergingChildConstraintMultipleTypes(string url, string elementId)
        {
            var resolver = new CachedResolver(new MultiResolver(_standardFhirSource, new TestProfileArtifactSource()));
            var snapshotGenerator = new SnapshotGenerator(resolver, SnapshotGeneratorSettings.CreateDefault());
            var sd = await resolver.ResolveByCanonicalUriAsync(url) as StructureDefinition;

            // Act
            var elements = await snapshotGenerator.GenerateAsync(sd);

            // Assert
            snapshotGenerator.Outcome.Should().BeNull();

            var extensionElement = elements.SingleOrDefault(x => x.ElementId == elementId);

            extensionElement.Should().NotBeNull();
        }

        [TestMethod]
        public void TestDistinctTypeCode()
        {
            var elem = new ElementDefinition();
            Assert.AreEqual(null, elem.CommonTypeCode());

            var patientTypeCode = FHIRAllTypes.Patient.GetLiteral();
            elem.Type.Add(new ElementDefinition.TypeRefComponent() { Code = patientTypeCode, Profile = new[] { @"http://example.org/fhir/StructureDefinition/MyPatient1" } });
            Assert.AreEqual(patientTypeCode, elem.CommonTypeCode());

            elem.Type.Add(new ElementDefinition.TypeRefComponent() { Code = patientTypeCode, Profile = new[] { @"http://example.org/fhir/StructureDefinition/MyPatient2" } });
            Assert.AreEqual(patientTypeCode, elem.CommonTypeCode());

            // Invalid, type constraint without type code (required!)
            elem.Type.Add(new ElementDefinition.TypeRefComponent() { Profile = new[] { @"http://example.org/fhir/StructureDefinition/MyPatient3" } });
            Assert.AreEqual(patientTypeCode, elem.CommonTypeCode());

            elem.Type.Add(new ElementDefinition.TypeRefComponent() { Code = FHIRAllTypes.Observation.GetLiteral(), Profile = new[] { @"http://example.org/fhir/StructureDefinition/MyObservation" } });
            Assert.IsNull(elem.CommonTypeCode());
        }

        /// <summary>
        /// Test cases that have non corrected values:
        /// [N1] Max lt Min
        /// Test cases that have corrected values:
        /// [C1] Max diff: * -> take Max snap: 1
        /// [C2] Min diff lte snap -> take Min snap: 1
        /// </summary>
        [TestMethod]
        [DataRow("TestExtension01", null, null, 0, "1", 0, "1")]
        [DataRow("TestExtension01", null, "0", 0, "0", 0, "1")]
        [DataRow("TestExtension01", null, "1", 0, "1", 0, "1")]
        [DataRow("TestExtension01", null, "*", 0, "1", 0, "1")] // [C1]
        [DataRow("TestExtension01", 0, null, 0, "1", 0, "1")]
        [DataRow("TestExtension01", 0, "0", 0, "0", 0, "1")]
        [DataRow("TestExtension01", 0, "1", 0, "1", 0, "1")]
        [DataRow("TestExtension01", 0, "*", 0, "1", 0, "1")] // [C1]
        [DataRow("TestExtension01", 1, null, 1, "1", 0, "1")]
        [DataRow("TestExtension01", 1, "0", 1, "0", 0, "1")] // [N1]
        [DataRow("TestExtension01", 1, "1", 1, "1", 0, "1")]
        [DataRow("TestExtension01", 1, "*", 1, "1", 0, "1")] // [C1]

        [DataRow("TestExtension11", null, null, 1, "1", 1, "1")]
        [DataRow("TestExtension11", null, "0", 1, "0", 1, "1")] // [N1]
        [DataRow("TestExtension11", null, "1", 1, "1", 1, "1")]
        [DataRow("TestExtension11", null, "*", 1, "1", 1, "1")] // [C1]
        [DataRow("TestExtension11", 0, null, 1, "1", 1, "1")] // [C2]
        [DataRow("TestExtension11", 0, "0", 1, "0", 1, "1")] // [C2][N1]
        [DataRow("TestExtension11", 0, "1", 1, "1", 1, "1")] // [C2]
        [DataRow("TestExtension11", 0, "*", 1, "1", 1, "1")] // [C2][C1]
        [DataRow("TestExtension11", 1, null, 1, "1", 1, "1")]
        [DataRow("TestExtension11", 1, "0", 1, "0", 1, "1")] // [N1]
        [DataRow("TestExtension11", 1, "1", 1, "1", 1, "1")]
        [DataRow("TestExtension11", 1, "*", 1, "1", 1, "1")] // [C1]

        [DataRow("TestExtension0star", null, null, 0, "*", 0, "*")]
        [DataRow("TestExtension0star", null, "0", 0, "0", 0, "*")]
        [DataRow("TestExtension0star", null, "1", 0, "1", 0, "*")]
        [DataRow("TestExtension0star", null, "2", 0, "2", 0, "*")]
        [DataRow("TestExtension0star", null, "*", 0, "*", 0, "*")]
        [DataRow("TestExtension0star", 0, null, 0, "*", 0, "*")]
        [DataRow("TestExtension0star", 0, "0", 0, "0", 0, "*")]
        [DataRow("TestExtension0star", 0, "1", 0, "1", 0, "*")]
        [DataRow("TestExtension0star", 0, "2", 0, "2", 0, "*")]
        [DataRow("TestExtension0star", 0, "*", 0, "*", 0, "*")]
        [DataRow("TestExtension0star", 1, null, 1, "*", 0, "*")]
        [DataRow("TestExtension0star", 1, "0", 1, "0", 0, "*")] // [N1]
        [DataRow("TestExtension0star", 1, "1", 1, "1", 0, "*")]
        [DataRow("TestExtension0star", 1, "2", 1, "2", 0, "*")]
        [DataRow("TestExtension0star", 1, "*", 1, "*", 0, "*")]
        [DataRow("TestExtension0star", 2, null, 2, "*", 0, "*")]
        [DataRow("TestExtension0star", 2, "0", 2, "0", 0, "*")] // [N1]
        [DataRow("TestExtension0star", 2, "1", 2, "1", 0, "*")] // [N1]
        [DataRow("TestExtension0star", 2, "2", 2, "2", 0, "*")]
        [DataRow("TestExtension0star", 2, "*", 2, "*", 0, "*")]

        [DataRow("TestExtension1star", null, null, 1, "*", 1, "*")]
        [DataRow("TestExtension1star", null, "0", 1, "0", 1, "*")] // [N1]
        [DataRow("TestExtension1star", null, "1", 1, "1", 1, "*")]
        [DataRow("TestExtension1star", null, "2", 1, "2", 1, "*")]
        [DataRow("TestExtension1star", null, "*", 1, "*", 1, "*")]
        [DataRow("TestExtension1star", 0, null, 1, "*", 1, "*")] // [C2]
        [DataRow("TestExtension1star", 0, "0", 1, "0", 1, "*")] // [C2][N1]
        [DataRow("TestExtension1star", 0, "1", 1, "1", 1, "*")] // [C2]
        [DataRow("TestExtension1star", 0, "2", 1, "2", 1, "*")] // [C2]
        [DataRow("TestExtension1star", 0, "*", 1, "*", 1, "*")] // [C2]
        [DataRow("TestExtension1star", 1, null, 1, "*", 1, "*")]
        [DataRow("TestExtension1star", 1, "0", 1, "0", 1, "*")] // [N1]
        [DataRow("TestExtension1star", 1, "1", 1, "1", 1, "*")]
        [DataRow("TestExtension1star", 1, "2", 1, "2", 1, "*")]
        [DataRow("TestExtension1star", 1, "*", 1, "*", 1, "*")]
        [DataRow("TestExtension1star", 2, null, 2, "*", 1, "*")]
        [DataRow("TestExtension1star", 2, "0", 2, "0", 1, "*")] // [N1]
        [DataRow("TestExtension1star", 2, "1", 2, "1", 1, "*")] // [N1]
        [DataRow("TestExtension1star", 2, "2", 2, "2", 1, "*")]
        [DataRow("TestExtension1star", 2, "*", 2, "*", 1, "*")]
        public async Tasks.Task CardinalityOfExtension(string extension, int? diffMin, string diffMax, int extMin, string extMax, int baseMin, string baseMax)
        {
            // Arrange
            string url = $"https://example.org/fhir/StructureDefinition/issue-1981-patient";
            string parentId = "Patient.extension";
            string elementId = "Patient.extension:test";

            var sd = await _testResolver.FindStructureDefinitionAsync(url);

            sd.Differential.Element.Should().HaveCount(2);

            var extensionElement = sd.Differential.Element.Single(x => x.ElementId == elementId);

            extensionElement.Min = diffMin;
            extensionElement.Max = diffMax;
            extensionElement.Type[0].ProfileElement[0].Value = $"https://example.org/fhir/StructureDefinition/{extension}";

            var snapshotGenerator = new SnapshotGenerator(_testResolver, _settings);

            snapshotGenerator.PrepareElement += delegate (object _, SnapshotElementEventArgs e)
            {
                e.Element.Should().NotBeNull();

                if (e.Element.Annotation<TestAnnotation>() != null)
                    e.Element.RemoveAnnotations<TestAnnotation>();

                e.Element.AddAnnotation(new TestAnnotation(e.BaseStructure, e.BaseElement));
            };

            var elements = await snapshotGenerator.GenerateAsync(sd);

            snapshotGenerator.Outcome.Should().BeNull();

            var parentElement = elements.Single(x => x.ElementId == parentId);

            // Act
            var elementsExpanded = await snapshotGenerator.ExpandElementAsync(elements, parentElement);

            // Assert
            extensionElement = elementsExpanded.Single(x => x.ElementId == elementId);

            extensionElement.Min.Should().Be(extMin);
            extensionElement.Max.Should().Be(extMax);

            var baseElement = extensionElement.Annotation<TestAnnotation>().BaseElementDefinition;

            baseElement.Min.Should().Be(baseMin);
            baseElement.Max.Should().Be(baseMax);
        }

        private sealed class TestAnnotation
        {
            public TestAnnotation(StructureDefinition baseStructure, ElementDefinition baseElemDef)
            {
                BaseStructureDefinition = baseStructure;
                BaseElementDefinition = baseElemDef;
            }

            public StructureDefinition BaseStructureDefinition { get; }
            public ElementDefinition BaseElementDefinition { get; }
        }

        public static IEnumerable<object[]> ElementDefinitionPropertyExtensionTestCasesR4
        {
            get
            {
                // Modify an existing Binding extension
                yield return new object[] { FHIRAllTypes.AllergyIntolerance, "AllergyIntolerance.code", "Binding",
                    new[] {
                        new Extension("http://hl7.org/fhir/StructureDefinition/elementdefinition-bindingName", new FhirString("AllergyIntoleranceCode")) },
                    new[] {
                        new Extension("http://hl7.org/fhir/StructureDefinition/elementdefinition-bindingName", new FhirString("Test")) }};

                // Adding a new Binding extension
                yield return new object[] { FHIRAllTypes.AllergyIntolerance, "AllergyIntolerance.code", "Binding",
                    new[] {
                        new Extension("http://hl7.org/fhir/StructureDefinition/elementdefinition-bindingName", new FhirString("AllergyIntoleranceCode")) },
                    new[] {
                        new Extension("http://hl7.org/fhir/StructureDefinition/elementdefinition-isCommonBinding", new FhirBoolean(true)) }};

                // Adding a new Constraint extension
                yield return new object[] { FHIRAllTypes.AllergyIntolerance, "AllergyIntolerance", "Constraint[Key:dom-2]",
                    Array.Empty<Extension>(),
                    new[] {
                        new Extension("http://hl7.org/fhir/StructureDefinition/elementdefinition-bestpractice", new FhirBoolean(true)) }};

                // Modifying an existing Constraint extension
                yield return new object[] { FHIRAllTypes.AllergyIntolerance, "AllergyIntolerance", "Constraint[Key:dom-6]",
                    new[] {
                        new Extension("http://hl7.org/fhir/StructureDefinition/elementdefinition-bestpractice", new FhirBoolean(true)),
                        new Extension("http://hl7.org/fhir/StructureDefinition/elementdefinition-bestpractice-explanation", new Markdown("When a resource has no narrative, only systems that fully understand the data can display the resource to a human safely. Including a human readable representation in the resource makes for a much more robust eco-system and cheaper handling of resources by intermediary systems. Some ecosystems restrict distribution of resources to only those systems that do fully understand the resources, and as a consequence implementers may believe that the narrative is superfluous. However experience shows that such eco-systems often open up to new participants over time.")) },
                    new[] {
                        new Extension("http://hl7.org/fhir/StructureDefinition/elementdefinition-bestpractice", new FhirBoolean(false)) }};
            }
        }

        /// <summary>
        /// Tests whether element definition property extensions in the differential are properly merged by the snapshot generator.
        /// </summary>
        /// <param name="profileType">The profile type under test (e.g. FHIRAllTypes.AllergyIntolerance).</param>
        /// <param name="elementId">The element id of the profile to check (e.g. "AllergyIntolerance.code")</param>
        /// <param name="propertyName">The name of the element definition property for which to add or modify the extension in the differential (e.g. "Binding").</param>
        /// <param name="baseExtensions">The extensions that are defined in the base profile for this property.</param>
        /// <param name="diffExtensions">The extensions to define in the differential for this property.</param>
        /// <returns></returns>
        [DataTestMethod]
        [DynamicData(nameof(ElementDefinitionPropertyExtensionTestCasesR4), DynamicDataSourceType.Property)]
        public async Tasks.Task ElementDefinitionPropertyExtensionTest(FHIRAllTypes profileType, string elementId, string propertyName, Extension[] baseExtensions, Extension[] diffExtensions)
        {
            // Arrange
            var uri = ModelInfo.CanonicalUriForFhirCoreType(profileType);
            var generator = new SnapshotGenerator(_standardFhirSource, SnapshotGeneratorSettings.CreateDefault());
            var propertyProxy = new ElementDefinitionPropertyProxy(propertyName);

            var sd = await _testResolver.FindStructureDefinitionAsync(uri); // Find base profile
            var snapElementDefinition = sd.Snapshot.Element.SingleOrDefault(x => x.ElementId == elementId); // Find specified element in snapshot of base profile
            snapElementDefinition.Should().NotBeNull();
            var snapElementDefinitionProperty = propertyProxy.GetValueAsElement(snapElementDefinition); // Get the property from the snapshot element (typed)

            if (snapElementDefinitionProperty != null)
            {
                logExtensions("Base extensions", baseExtensions);

                snapElementDefinitionProperty.Extension.Should().HaveCount(baseExtensions.Length); // Check extensions in element
                snapElementDefinitionProperty.Extension.OrderBy(x => x.Url).Should().Equal(baseExtensions.OrderBy(x => x.Url), (e1, e2) => e1.IsExactly(e2));
            }
            else
            {
                baseExtensions.Should().BeNull();
            }

            var diffElementDefinition = new ElementDefinition(elementId) { ElementId = elementId }; // Create element for differential
            propertyProxy.SetValue(diffElementDefinition, propertyProxy.CreateInstance(snapElementDefinitionProperty)); // Set property for differential element
            var diffElementDefinitionProperty = propertyProxy.GetValueAsElement(diffElementDefinition); // Get the property from the differential element (typed)

            // Add extensions for the element definition properties in the differential
            foreach (Extension diffExtension in diffExtensions)
            {
                var extension = diffElementDefinitionProperty.Extension.SingleOrDefault(x => x.Url == diffExtension.Url);

                if (extension != null)
                {
                    var index = diffElementDefinitionProperty.Extension.IndexOf(extension);
                    diffElementDefinitionProperty.Extension[index] = diffExtension; // Modification on base extension
                }
                else
                {
                    diffElementDefinitionProperty.Extension.AddRange(diffExtensions); // New extension (i.e. does not exist in base)
                }
            }

            var profile = new StructureDefinition() // Create derived profile
            {
                Type = profileType.GetLiteral(),
                BaseDefinition = uri,
                Name = "My" + elementId,
                Url = uri + "Test",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>() { diffElementDefinition }
                }
            };

            // Act
            var elements = await generator.GenerateAsync(profile);

            // Assert
            var element = elements.SingleOrDefault(x => x.ElementId == diffElementDefinition.ElementId);
            element.Should().NotBeNull();
            var elementProperty = propertyProxy.GetValueAsElement(element);

            var expectedExtensions = new List<Extension>(diffElementDefinitionProperty.Extension); // Determine expected extensions

            foreach (Extension snapExtension in snapElementDefinitionProperty.Extension)
            {
                if (expectedExtensions.SingleOrDefault(x => x.Url == snapExtension.Url) == null)
                    expectedExtensions.Add(snapExtension); // Extension from snapshot element property should be in expected extensions
            }

            logExtensions("Expected extensions", expectedExtensions);
            logExtensions("Actual extensions", elementProperty.Extension);

            elementProperty.Extension.Should().HaveCount(expectedExtensions.Count);
            elementProperty.Extension.OrderBy(x => x.Url).Should().Equal(expectedExtensions.OrderBy(x => x.Url), (e1, e2) => e1.IsExactly(e2));
        }

        [TestMethod]
        public async Tasks.Task ConstrainChoiceTypeWithExplicitSlicesInDerivedProfileCorrectly()
        {
            var baseStructureDefinition = new StructureDefinition()
            {
                Url = "http://fire.ly/fhir/StructureDefiniton/ObservationBaseLimitedChoiceTypes",
                Name = "ObservationBaseLimitedChoiceTypes",
                Status = PublicationStatus.Active,
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Abstract = false,
                Type = "Observation",
                BaseDefinition = "http://hl7.org/fhir/StructureDefinition/Observation",
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition()
                        {
                            Path = "Observation.value[x]",
                            ElementId = "Observation.value[x]",
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent() {Code = "Quantity"},
                                new ElementDefinition.TypeRefComponent() {Code = "CodeableConcept"}
                            },
                            Slicing = new ElementDefinition.SlicingComponent
                            {
                                Discriminator = new List<ElementDefinition.DiscriminatorComponent>
                                {
                                    new ElementDefinition.DiscriminatorComponent
                                    {
                                        Path = "$this",
                                        Type = ElementDefinition.DiscriminatorType.Type
                                    }
                                },
                                Ordered = false,
                                Rules = ElementDefinition.SlicingRules.Closed
                            }
                        },
                        new ElementDefinition()
                        {
                            Path = "Observation.value[x]",
                            ElementId = "Observation.value[x]:valueQuantity",
                            SliceName = "valueQuantity",
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent() {Code = "Quantity"},
                            }
                        },
                         new ElementDefinition()
                        {
                            Path = "Observation.value[x].system",
                            ElementId = "Observation.value[x]:valueQuantity.system",
                            Fixed = new FhirUri("http://unitsofmeasure.org")
                        },
                        new ElementDefinition()
                        {
                            Path = "Observation.value[x]",
                            ElementId = "Observation.value[x]:valueCodeableConcept",
                            SliceName = "valueCodeableConcept",
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent() {Code = "CodeableConcept"},
                            }
                        },
                        new ElementDefinition()
                        {
                            Path = "Observation.value[x].system",
                            ElementId = "Observation.value[x]:valueCodeableConcept.system",
                            Fixed = new FhirUri("http://fire.ly/fhir/sid/test")
                        }
                    }
                }
            };

            var derivedStructureDefinition = new StructureDefinition()
            {
                Url = "http://fire.ly/fhir/StructureDefiniton/ObservationDerivedLimitedChoiceTypes",
                Name = "ObservationBaseDerivedChoiceTypes",
                Status = PublicationStatus.Active,
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Abstract = false,
                Type = "Observation",
                BaseDefinition = "http://fire.ly/fhir/StructureDefiniton/ObservationBaseLimitedChoiceTypes",
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition()
                        {
                            Path = "Observation.value[x]",
                            ElementId = "Observation.value[x]",
                            Type = new List<ElementDefinition.TypeRefComponent>()
                            {
                                new ElementDefinition.TypeRefComponent() {Code = "CodeableConcept"}
                            }
                        }
                    }
                }
            };

            var resolver = new InMemoryResourceResolver(baseStructureDefinition, derivedStructureDefinition);
            var multiResolver = new MultiResolver(_testResolver, resolver);
            _generator = new SnapshotGenerator(multiResolver, _settings);

            var elementDefinitions = await _generator.GenerateAsync(derivedStructureDefinition);
            var valuexEld = elementDefinitions.First(eld => "Observation.value[x]".Equals((eld.ElementId)));
            Assert.AreEqual(1, valuexEld.Type.Count);
            Assert.AreEqual("CodeableConcept", valuexEld.Type.First().Code);

            var valueQuantityEld = elementDefinitions.FirstOrDefault(eld => "Observation.value[x]:valueQuantity".Equals((eld.ElementId)));
            Assert.IsNull(valueQuantityEld);
        }

        [TestMethod]
        public async Tasks.Task BindingRemovedAfterTypeSlicing()
        {
            // Arrange
            var resolver = new CachedResolver(
                    new MultiResolver(
                        new TestProfileArtifactSource(),
                        _standardFhirSource));

            string url = $"http://validationtest.org/fhir/StructureDefinition/MedicationRequest-issue-2132";

            var sd = await resolver.FindStructureDefinitionAsync(url);

            var snapshotGenerator = new SnapshotGenerator(resolver, _settings);

            var elements = await snapshotGenerator.GenerateAsync(sd);

            var element = elements.Should().ContainSingle(e => e.ElementId == "MedicationRequest.substitution.allowed[x]:allowedBoolean").Subject;
            element.Type.Should().OnlyContain(t => t.Code == "boolean");
            element.Binding.Should().BeNull();

            element = elements.Should().ContainSingle(e => e.ElementId == "MedicationRequest.substitution.allowed[x]:allowedCodeableConcept").Subject;
            element.Type.Should().OnlyContain(t => t.Code == "CodeableConcept");
            element.Binding.Should().NotBeNull();

        }

        [TestMethod]
        public async Tasks.Task BindingRemovedAfterTypeConstraint()
        {
            // Arrange
            var resolver = new CachedResolver(
                    new MultiResolver(
                        new TestProfileArtifactSource(),
                        _standardFhirSource));

            string url = $"http://validationtest.org/fhir/StructureDefinition/MedicationRequest-issue-2132-2";

            var sd = await resolver.FindStructureDefinitionAsync(url);

            var snapshotGenerator = new SnapshotGenerator(resolver, _settings);

            var elements = await snapshotGenerator.GenerateAsync(sd);

            var element = elements.Should().ContainSingle(e => e.Path == "MedicationRequest.substitution.allowed[x]").Subject;
            element.Type.Should().OnlyContain(t => t.Code == "boolean");
            element.Binding.Should().BeNull();
        }

        [TestMethod]
        public async Tasks.Task TestConstraintSource()
        {
            var observation = await _testResolver.FindStructureDefinitionAsync("http://hl7.org/fhir/StructureDefinition/Observation");
            _generator = new SnapshotGenerator(_testResolver, _settings);

            var snapshot = await _generator.GenerateAsync(observation);

            var element = snapshot.Should().Contain(e => e.Path == "Observation.subject").Subject;
            var constraint = element.Constraint.Where(c => c.Key == "ref-1").FirstOrDefault();
            constraint.Source.Should().Be("http://hl7.org/fhir/StructureDefinition/Reference");
        }

        private void logExtensions(string title, IEnumerable<Extension> extensions, int level = 1)
        {
            Debug.WriteLine(title);

            if (!extensions.Any())
            {
                Debug.WriteLine($"{new string(' ', level * 3)}none");
                return;
            }

            foreach (Extension extension in extensions)
            {
                if (extension.Extension != null && extension.Extension.Count > 0)
                    logExtensions(extension.Url, extension.Extension, level + 1);
                else
                    Debug.WriteLine($"{new string(' ', level * 3)}{extension.Url} : {extension.Value}");
            }
        }

        //Tests Github issue #2211, see TestData/Issue-2211 for test artifacts.
        [TestMethod]
        public async Tasks.Task TestMergingAPreviouslyRemovedElement()
        {
            var structure = await _testResolver.FindStructureDefinitionAsync("http://fire.ly/fhir/StructureDefiniton/ObservationDerivedLimitedChoiceTypes");
            _generator = new SnapshotGenerator(_testResolver, _settings);

            var elementDefinitions = await _generator.GenerateAsync(structure);
            var valuexEld = elementDefinitions.First(eld => "Observation.value[x]".Equals((eld.ElementId)));
            Assert.AreEqual(1, valuexEld.Type.Count);
            Assert.AreEqual("CodeableConcept", valuexEld.Type.First().Code);

            var valueQuantityEld = elementDefinitions.FirstOrDefault(eld => "Observation.value[x]:valueQuantity".Equals((eld.ElementId)));
            Assert.IsNull(valueQuantityEld);
        }

        [TestMethod]
        public async Tasks.Task TestNewR5Elements()
        {
            var sd = createR5StructureDefinition();
            var resolver = new InMemoryResourceResolver(sd);
            var multiResolver = new MultiResolver(_testResolver, resolver);

            _generator = new SnapshotGenerator(multiResolver, _settings);

            var snapshot = await _generator.GenerateAsync(sd);
            var gender = snapshot.Where(e => e.Path == "Patient.gender").FirstOrDefault();
            gender.Should().NotBeNull();

            gender.SliceIsConstraining.Should().BeFalse();
            gender.MustHaveValue.Should().BeTrue();
            gender.ValueAlternatives.Should().Contain("foo");
            gender.ValueAlternatives.Should().Contain("bar");
            gender.Binding.Additional.FirstOrDefault().Purpose.Should().Be(ElementDefinition.AdditionalBindingPurposeVS.Maximum);
            gender.Binding.Additional.FirstOrDefault().Documentation.Should().Be("foo");

        }

        private StructureDefinition createR5StructureDefinition()
        {
            return new StructureDefinition
            {
                Type = FHIRAllTypes.Patient.GetLiteral(),
                BaseDefinition = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient),
                Name = "PatientWithR5Features",
                Url = @"http://example.org/fhir/StructureDefinition/PatientWithR5Features",
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Differential = new StructureDefinition.DifferentialComponent()
                {
                    Element = new List<ElementDefinition>()
                    {
                        new ElementDefinition("Patient.gender")
                        {
                            SliceIsConstraining = false,
                            MustHaveValue = true,
                            ValueAlternatives = new List<string>()
                            {
                                "foo",
                                "bar"
                            },
                            Binding = new()
                            {
                                Strength = BindingStrength.Required,
                                Description = "The gender of a person used for administrative purposes",
                                ValueSet = "http://hl7.org/fhir/ValueSet/administrative-gender|5.0.0",
                                Additional = new List<ElementDefinition.AdditionalComponent>
                                {
                                    new()
                                    {
                                        Purpose = ElementDefinition.AdditionalBindingPurposeVS.Maximum,
                                        ValueSet = "http://example.org/fhir/ValueSet/additional-binding",
                                        Documentation = "foo",
                                        ShortDoco = "bar",
                                        Any = true
                                    }
                                }
                            }

                        }
                    }
                }
            };
        }
    }
}