/*
  Copyright (c) 2011-2012, HL7, Inc
  All rights reserved.
  
  Redistribution and use in source and binary forms, with or without modification, 
  are permitted provided that the following conditions are met:
  
   * Redistributions of source code must retain the above copyright notice, this 
     list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above copyright notice, 
     this list of conditions and the following disclaimer in the documentation 
     and/or other materials provided with the distribution.
   * Neither the name of HL7 nor the names of its contributors may be used to 
     endorse or promote products derived from this software without specific 
     prior written permission.
  
  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND 
  ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED 
  WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
  IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, 
  INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT 
  NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR 
  PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, 
  WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
  ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
  POSSIBILITY OF SUCH DAMAGE.
  
*/

using System;
using System.Linq;

namespace Hl7.Fhir.Model
{
    public partial class SearchParameter : IVersionableConformanceResource
    {

    }

    public partial class OperationDefinition : IVersionableConformanceResource
    {

    }

    public partial class MessageDefinition : IVersionableConformanceResource
    {

    }

    public partial class ImplementationGuide : IVersionableConformanceResource
    {
        //I think ImplementationGuide should have a purpose element.
        [Obsolete("This property is not a part of the official FHIR specification", true)]
        public Markdown PurposeElement
        {
            get { return null; }
            set { throw new NotImplementedException(); }
        }

        public string Purpose { get => null; set => throw new NotImplementedException(); }
    }

    public partial class CompartmentDefinition : IVersionableConformanceResource
    {

    }
    public partial class StructureMap : IVersionableConformanceResource
    {

    }
    public partial class GraphDefinition : IVersionableConformanceResource
    {

    }

    public partial class ConceptMap : IVersionableConformanceResource
    {

    }
    public partial class TestScript : IVersionableConformanceResource
    {

    }

    public partial class Questionnaire : IVersionableConformanceResource
    {

    }

    public partial class TerminologyCapabilities : IVersionableConformanceResource
    {

    }
    
    public partial class Library : IVersionableConformanceResource
    {

    }

    public partial class NamingSystem : IConformanceResource
    {
        // I think NamingSystem should have Experimental too
        [Obsolete("This property is not a part of the official FHIR specification", true)]
        public Markdown PurposeElement
        {
            get { return null; }
            set { throw new NotImplementedException(); }
        }

        public bool? Experimental
        {
            get { return null; }
            set { throw new NotImplementedException(); }
        }

        public FhirBoolean ExperimentalElement
        {
            get { return null; }
            set { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Will return the (first) preferred UniqueId, or the first UniqueId if there is no preferred UniqueId
        /// </summary>
        public string Url
        {
            get
            {
                var preferred = UniqueId.FirstOrDefault(id => id.Preferred == true)?.Value;
                return preferred ?? UniqueId.FirstOrDefault()?.Value;
            }
            set { throw new NotImplementedException(); }
        }

        public FhirUri UrlElement
        {
            get
            {
                if (Url != null)
                    return new FhirUri(Url);
                else
                    return null;
            }
            set { throw new NotImplementedException(); }
        }

        public string Purpose { get => null; set => throw new NotImplementedException(); }
    }
}
