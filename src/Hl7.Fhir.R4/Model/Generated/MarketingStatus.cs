// <auto-generated/>
// Contents of: hl7.fhir.r4.core version: 4.0.1

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Hl7.Fhir.Utility;
using Hl7.Fhir.Validation;

/*
  Copyright (c) 2011+, HL7, Inc.
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

namespace Hl7.Fhir.Model
{
  /// <summary>
  /// The marketing status describes the date when a medicinal product is actually put on the market or the date as of which it is no longer available
  /// </summary>
  [Serializable]
  [DataContract]
  [FhirType("MarketingStatus","http://hl7.org/fhir/StructureDefinition/MarketingStatus")]
  public partial class MarketingStatus : Hl7.Fhir.Model.BackboneType
  {
    /// <summary>
    /// FHIR Type Name
    /// </summary>
    public override string TypeName { get { return "MarketingStatus"; } }

    /// <summary>
    /// The country in which the marketing authorisation has been granted shall be specified It should be specified using the ISO 3166 ‑ 1 alpha-2 code elements
    /// </summary>
    [FhirElement("country", InSummary=true, Order=40)]
    [Cardinality(Min=1,Max=1)]
    [DataMember]
    public Hl7.Fhir.Model.CodeableConcept Country
    {
      get { return _Country; }
      set { _Country = value; OnPropertyChanged("Country"); }
    }

    private Hl7.Fhir.Model.CodeableConcept _Country;

    /// <summary>
    /// Where a Medicines Regulatory Agency has granted a marketing authorisation for which specific provisions within a jurisdiction apply, the jurisdiction can be specified using an appropriate controlled terminology The controlled term and the controlled term identifier shall be specified
    /// </summary>
    [FhirElement("jurisdiction", InSummary=true, Order=50)]
    [DataMember]
    public Hl7.Fhir.Model.CodeableConcept Jurisdiction
    {
      get { return _Jurisdiction; }
      set { _Jurisdiction = value; OnPropertyChanged("Jurisdiction"); }
    }

    private Hl7.Fhir.Model.CodeableConcept _Jurisdiction;

    /// <summary>
    /// This attribute provides information on the status of the marketing of the medicinal product See ISO/TS 20443 for more information and examples
    /// </summary>
    [FhirElement("status", InSummary=true, Order=60)]
    [Cardinality(Min=1,Max=1)]
    [DataMember]
    public Hl7.Fhir.Model.CodeableConcept Status
    {
      get { return _Status; }
      set { _Status = value; OnPropertyChanged("Status"); }
    }

    private Hl7.Fhir.Model.CodeableConcept _Status;

    /// <summary>
    /// The date when the Medicinal Product is placed on the market by the Marketing Authorisation Holder (or where applicable, the manufacturer/distributor) in a country and/or jurisdiction shall be provided A complete date consisting of day, month and year shall be specified using the ISO 8601 date format NOTE “Placed on the market” refers to the release of the Medicinal Product into the distribution chain
    /// </summary>
    [FhirElement("dateRange", InSummary=true, Order=70)]
    [Cardinality(Min=1,Max=1)]
    [DataMember]
    public Hl7.Fhir.Model.Period DateRange
    {
      get { return _DateRange; }
      set { _DateRange = value; OnPropertyChanged("DateRange"); }
    }

    private Hl7.Fhir.Model.Period _DateRange;

    /// <summary>
    /// The date when the Medicinal Product is placed on the market by the Marketing Authorisation Holder (or where applicable, the manufacturer/distributor) in a country and/or jurisdiction shall be provided A complete date consisting of day, month and year shall be specified using the ISO 8601 date format NOTE “Placed on the market” refers to the release of the Medicinal Product into the distribution chain
    /// </summary>
    [FhirElement("restoreDate", InSummary=true, Order=80)]
    [DataMember]
    public Hl7.Fhir.Model.FhirDateTime RestoreDateElement
    {
      get { return _RestoreDateElement; }
      set { _RestoreDateElement = value; OnPropertyChanged("RestoreDateElement"); }
    }

    private Hl7.Fhir.Model.FhirDateTime _RestoreDateElement;

    /// <summary>
    /// The date when the Medicinal Product is placed on the market by the Marketing Authorisation Holder (or where applicable, the manufacturer/distributor) in a country and/or jurisdiction shall be provided A complete date consisting of day, month and year shall be specified using the ISO 8601 date format NOTE “Placed on the market” refers to the release of the Medicinal Product into the distribution chain
    /// </summary>
    /// <remarks>This uses the native .NET datatype, rather than the FHIR equivalent</remarks>
    [IgnoreDataMember]
    public string RestoreDate
    {
      get { return RestoreDateElement != null ? RestoreDateElement.Value : null; }
      set
      {
        if (value == null)
          RestoreDateElement = null;
        else
          RestoreDateElement = new Hl7.Fhir.Model.FhirDateTime(value);
        OnPropertyChanged("RestoreDate");
      }
    }

    public override IDeepCopyable CopyTo(IDeepCopyable other)
    {
      var dest = other as MarketingStatus;

      if (dest == null)
      {
        throw new ArgumentException("Can only copy to an object of the same type", "other");
      }

      base.CopyTo(dest);
      if(Country != null) dest.Country = (Hl7.Fhir.Model.CodeableConcept)Country.DeepCopy();
      if(Jurisdiction != null) dest.Jurisdiction = (Hl7.Fhir.Model.CodeableConcept)Jurisdiction.DeepCopy();
      if(Status != null) dest.Status = (Hl7.Fhir.Model.CodeableConcept)Status.DeepCopy();
      if(DateRange != null) dest.DateRange = (Hl7.Fhir.Model.Period)DateRange.DeepCopy();
      if(RestoreDateElement != null) dest.RestoreDateElement = (Hl7.Fhir.Model.FhirDateTime)RestoreDateElement.DeepCopy();
      return dest;
    }

    public override IDeepCopyable DeepCopy()
    {
      return CopyTo(new MarketingStatus());
    }

    ///<inheritdoc />
    public override bool Matches(IDeepComparable other)
    {
      var otherT = other as MarketingStatus;
      if(otherT == null) return false;

      if(!base.Matches(otherT)) return false;
      if( !DeepComparable.Matches(Country, otherT.Country)) return false;
      if( !DeepComparable.Matches(Jurisdiction, otherT.Jurisdiction)) return false;
      if( !DeepComparable.Matches(Status, otherT.Status)) return false;
      if( !DeepComparable.Matches(DateRange, otherT.DateRange)) return false;
      if( !DeepComparable.Matches(RestoreDateElement, otherT.RestoreDateElement)) return false;

      return true;
    }

    public override bool IsExactly(IDeepComparable other)
    {
      var otherT = other as MarketingStatus;
      if(otherT == null) return false;

      if(!base.IsExactly(otherT)) return false;
      if( !DeepComparable.IsExactly(Country, otherT.Country)) return false;
      if( !DeepComparable.IsExactly(Jurisdiction, otherT.Jurisdiction)) return false;
      if( !DeepComparable.IsExactly(Status, otherT.Status)) return false;
      if( !DeepComparable.IsExactly(DateRange, otherT.DateRange)) return false;
      if( !DeepComparable.IsExactly(RestoreDateElement, otherT.RestoreDateElement)) return false;

      return true;
    }

    [IgnoreDataMember]
    public override IEnumerable<Base> Children
    {
      get
      {
        foreach (var item in base.Children) yield return item;
        if (Country != null) yield return Country;
        if (Jurisdiction != null) yield return Jurisdiction;
        if (Status != null) yield return Status;
        if (DateRange != null) yield return DateRange;
        if (RestoreDateElement != null) yield return RestoreDateElement;
      }
    }

    [IgnoreDataMember]
    public override IEnumerable<ElementValue> NamedChildren
    {
      get
      {
        foreach (var item in base.NamedChildren) yield return item;
        if (Country != null) yield return new ElementValue("country", Country);
        if (Jurisdiction != null) yield return new ElementValue("jurisdiction", Jurisdiction);
        if (Status != null) yield return new ElementValue("status", Status);
        if (DateRange != null) yield return new ElementValue("dateRange", DateRange);
        if (RestoreDateElement != null) yield return new ElementValue("restoreDate", RestoreDateElement);
      }
    }

    protected override bool TryGetValue(string key, out object value)
    {
      switch (key)
      {
        case "country":
          value = Country;
          return Country is not null;
        case "jurisdiction":
          value = Jurisdiction;
          return Jurisdiction is not null;
        case "status":
          value = Status;
          return Status is not null;
        case "dateRange":
          value = DateRange;
          return DateRange is not null;
        case "restoreDate":
          value = RestoreDateElement;
          return RestoreDateElement is not null;
        default:
          return base.TryGetValue(key, out value);
      }

    }

    protected override IEnumerable<KeyValuePair<string, object>> GetElementPairs()
    {
      foreach (var kvp in base.GetElementPairs()) yield return kvp;
      if (Country is not null) yield return new KeyValuePair<string,object>("country",Country);
      if (Jurisdiction is not null) yield return new KeyValuePair<string,object>("jurisdiction",Jurisdiction);
      if (Status is not null) yield return new KeyValuePair<string,object>("status",Status);
      if (DateRange is not null) yield return new KeyValuePair<string,object>("dateRange",DateRange);
      if (RestoreDateElement is not null) yield return new KeyValuePair<string,object>("restoreDate",RestoreDateElement);
    }

  }

}

// end of file
