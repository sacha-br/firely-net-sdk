﻿/* 
 * Copyright (c) 2018, Firely (info@fire.ly) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://github.com/FirelyTeam/firely-net-sdk/blob/master/LICENSE
 */

using Hl7.Fhir.Utility;
using System;

namespace Hl7.Fhir.Serialization
{
    /// <summary>Configuration settings for the <see cref="FhirXmlBuilder"/> class.</summary>
    public class FhirXmlSerializationSettings
    {
        /// <summary>
        /// When encountering a member without type information, just skip it instead of reporting an error.
        /// </summary>
        public bool IgnoreUnknownElements;

        /// <summary>
        /// Format the xml output when converted to a string.
        /// </summary>
        public bool Pretty { get; set; } // = false;

        /// <summary>
        /// Add new line at the end of the xml output when converted to a string.
        /// </summary>
        public bool AppendNewLine { get; set; } // = false;

        /// <summary>
        /// Trims whitespaces add the beginning and the end of xml value attributes
        /// </summary>
        public bool TrimWhitespaces { get; set; } = true;

        /// <summary>Default constructor. Creates a new <see cref="FhirXmlSerializationSettings"/> instance with default property values.</summary>
        public FhirXmlSerializationSettings() { }

        /// <summary>Clone constructor. Generates a new <see cref="FhirXmlSerializationSettings"/> instance initialized from the state of the specified instance.</summary>
        /// <exception cref="ArgumentNullException">The specified argument is <c>null</c>.</exception>
        public FhirXmlSerializationSettings(FhirXmlSerializationSettings other)
        {
            if (other == null) throw Error.ArgumentNull(nameof(other));
            other.CopyTo(this);
        }

        /// <summary>Copy all configuration settings to another instance.</summary>
        /// <param name="other">Another <see cref="FhirXmlSerializationSettings"/> instance.</param>
        /// <exception cref="ArgumentNullException">The specified argument is <c>null</c>.</exception>
        public void CopyTo(FhirXmlSerializationSettings other)
        {
            if (other == null) throw Error.ArgumentNull(nameof(other));

            other.IgnoreUnknownElements = IgnoreUnknownElements;
            other.Pretty = Pretty;
            other.AppendNewLine = AppendNewLine;
            other.TrimWhitespaces = TrimWhitespaces;
        }

        /// <summary>Creates a new <see cref="FhirXmlSerializationSettings"/> object that is a copy of the current instance.</summary>
        public FhirXmlSerializationSettings Clone() => new FhirXmlSerializationSettings(this);

        /// <summary>Creates a new <see cref="FhirXmlSerializationSettings"/> instance with default property values.</summary>
        public static FhirXmlSerializationSettings CreateDefault() => new FhirXmlSerializationSettings();
    }
}
