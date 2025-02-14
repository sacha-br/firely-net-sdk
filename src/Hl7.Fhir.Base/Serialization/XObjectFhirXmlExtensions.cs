﻿/* 
 * Copyright (c) 2018, Firely (info@fire.ly) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://github.com/FirelyTeam/firely-net-sdk/blob/master/LICENSE
 */

using Hl7.Fhir.Utility;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

#nullable enable

namespace Hl7.Fhir.Serialization
{
    internal static class XObjectFhirXmlExtensions
    {
        public static bool IsResourceName(this XName elementName, bool ignoreNameSpace = false) =>
            Char.IsUpper(elementName.LocalName, 0) && (ignoreNameSpace || elementName.Namespace == XmlNs.XFHIR);

        public static bool TryGetContainedResource(this XElement xe, [NotNullWhen(true)] out XElement? contained, bool ignoreNameSpace = false)
        {
            contained = null;

            if (xe.HasElements)
            {
                var candidate = xe.Elements().First();

                if (candidate.Name.IsResourceName(ignoreNameSpace))
                {
                    contained = candidate;
                    return true;
                }
            }

            // Not on a resource, no name to be found
            return false;
        }

        public static XObject? NextElementOrAttribute(this XObject current)
        {
            var scan = current.NextSibling();
            return scanToNextRelevantNode(scan);
        }

        public static XObject? FirstChildElementOrAttribute(this XObject current)
        {
            var scan = current.FirstChild();
            return scanToNextRelevantNode(scan);
        }


        private static XObject? scanToNextRelevantNode(this XObject? scan)
        {
            while (scan != null)
            {
                if (IsRelevantNode(scan))
                    break;
                scan = scan.NextSibling();
            }

            return scan;
        }

        public static bool IsRelevantNode(this XObject scan)
        {
            return scan.NodeType == XmlNodeType.Element ||
                   (scan is XAttribute attr && isRelevantAttribute(attr));
        }

        private static bool isRelevantAttribute(XAttribute a) =>
            !a.IsNamespaceDeclaration && a.Name != XmlNs.XSCHEMALOCATION;

        public static bool HasRelevantAttributes(this XElement scan) =>
            scan.Attributes().Any(a => isRelevantAttribute(a));

        public static string? GetValue(this XObject current)
        {
            if (current.AtXhtmlDiv())
                return ((XElement)current).ToString(SaveOptions.DisableFormatting);
            if (current.FirstChild() is XText)
                return current.Value();
            return current is XElement xelem ?
                xelem.Attribute("value")?.Value.Trim() : current.Value();

        }
    }
}
