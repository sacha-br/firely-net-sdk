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
  /// A person with a  formal responsibility in the provisioning of healthcare or related services
  /// </summary>
  [Serializable]
  [DataContract]
  [FhirType("Practitioner","http://hl7.org/fhir/StructureDefinition/Practitioner", IsResource=true)]
  public partial class Practitioner : Hl7.Fhir.Model.DomainResource, IIdentifiable<List<Identifier>>
  {
    /// <summary>
    /// FHIR Type Name
    /// </summary>
    public override string TypeName { get { return "Practitioner"; } }

    /// <summary>
    /// Certification, licenses, or training pertaining to the provision of care
    /// </summary>
    [Serializable]
    [DataContract]
    [FhirType("Practitioner#Qualification", IsNestedType=true)]
    [BackboneType("Practitioner.qualification")]
    public partial class QualificationComponent : Hl7.Fhir.Model.BackboneElement
    {
      /// <summary>
      /// FHIR Type Name
      /// </summary>
      public override string TypeName { get { return "Practitioner#Qualification"; } }

      /// <summary>
      /// An identifier for this qualification for the practitioner
      /// </summary>
      [FhirElement("identifier", Order=40)]
      [Cardinality(Min=0,Max=-1)]
      [DataMember]
      public List<Hl7.Fhir.Model.Identifier> Identifier
      {
        get { if(_Identifier==null) _Identifier = new List<Hl7.Fhir.Model.Identifier>(); return _Identifier; }
        set { _Identifier = value; OnPropertyChanged("Identifier"); }
      }

      private List<Hl7.Fhir.Model.Identifier> _Identifier;

      /// <summary>
      /// Coded representation of the qualification
      /// </summary>
      [FhirElement("code", Order=50)]
      [Binding("Qualification")]
      [Cardinality(Min=1,Max=1)]
      [DataMember]
      public Hl7.Fhir.Model.CodeableConcept Code
      {
        get { return _Code; }
        set { _Code = value; OnPropertyChanged("Code"); }
      }

      private Hl7.Fhir.Model.CodeableConcept _Code;

      /// <summary>
      /// Period during which the qualification is valid
      /// </summary>
      [FhirElement("period", Order=60)]
      [DataMember]
      public Hl7.Fhir.Model.Period Period
      {
        get { return _Period; }
        set { _Period = value; OnPropertyChanged("Period"); }
      }

      private Hl7.Fhir.Model.Period _Period;

      /// <summary>
      /// Organization that regulates and issues the qualification
      /// </summary>
      [FhirElement("issuer", Order=70)]
      [CLSCompliant(false)]
      [References("Organization")]
      [DataMember]
      public Hl7.Fhir.Model.ResourceReference Issuer
      {
        get { return _Issuer; }
        set { _Issuer = value; OnPropertyChanged("Issuer"); }
      }

      private Hl7.Fhir.Model.ResourceReference _Issuer;

      public override IDeepCopyable CopyTo(IDeepCopyable other)
      {
        var dest = other as QualificationComponent;

        if (dest == null)
        {
          throw new ArgumentException("Can only copy to an object of the same type", "other");
        }

        base.CopyTo(dest);
        if(Identifier != null) dest.Identifier = new List<Hl7.Fhir.Model.Identifier>(Identifier.DeepCopy());
        if(Code != null) dest.Code = (Hl7.Fhir.Model.CodeableConcept)Code.DeepCopy();
        if(Period != null) dest.Period = (Hl7.Fhir.Model.Period)Period.DeepCopy();
        if(Issuer != null) dest.Issuer = (Hl7.Fhir.Model.ResourceReference)Issuer.DeepCopy();
        return dest;
      }

      public override IDeepCopyable DeepCopy()
      {
        return CopyTo(new QualificationComponent());
      }

      ///<inheritdoc />
      public override bool Matches(IDeepComparable other)
      {
        var otherT = other as QualificationComponent;
        if(otherT == null) return false;

        if(!base.Matches(otherT)) return false;
        if( !DeepComparable.Matches(Identifier, otherT.Identifier)) return false;
        if( !DeepComparable.Matches(Code, otherT.Code)) return false;
        if( !DeepComparable.Matches(Period, otherT.Period)) return false;
        if( !DeepComparable.Matches(Issuer, otherT.Issuer)) return false;

        return true;
      }

      public override bool IsExactly(IDeepComparable other)
      {
        var otherT = other as QualificationComponent;
        if(otherT == null) return false;

        if(!base.IsExactly(otherT)) return false;
        if( !DeepComparable.IsExactly(Identifier, otherT.Identifier)) return false;
        if( !DeepComparable.IsExactly(Code, otherT.Code)) return false;
        if( !DeepComparable.IsExactly(Period, otherT.Period)) return false;
        if( !DeepComparable.IsExactly(Issuer, otherT.Issuer)) return false;

        return true;
      }

      [IgnoreDataMember]
      public override IEnumerable<Base> Children
      {
        get
        {
          foreach (var item in base.Children) yield return item;
          foreach (var elem in Identifier) { if (elem != null) yield return elem; }
          if (Code != null) yield return Code;
          if (Period != null) yield return Period;
          if (Issuer != null) yield return Issuer;
        }
      }

      [IgnoreDataMember]
      public override IEnumerable<ElementValue> NamedChildren
      {
        get
        {
          foreach (var item in base.NamedChildren) yield return item;
          foreach (var elem in Identifier) { if (elem != null) yield return new ElementValue("identifier", elem); }
          if (Code != null) yield return new ElementValue("code", Code);
          if (Period != null) yield return new ElementValue("period", Period);
          if (Issuer != null) yield return new ElementValue("issuer", Issuer);
        }
      }

      protected override bool TryGetValue(string key, out object value)
      {
        switch (key)
        {
          case "identifier":
            value = Identifier;
            return Identifier?.Any() == true;
          case "code":
            value = Code;
            return Code is not null;
          case "period":
            value = Period;
            return Period is not null;
          case "issuer":
            value = Issuer;
            return Issuer is not null;
          default:
            return base.TryGetValue(key, out value);
        }

      }

      protected override IEnumerable<KeyValuePair<string, object>> GetElementPairs()
      {
        foreach (var kvp in base.GetElementPairs()) yield return kvp;
        if (Identifier?.Any() == true) yield return new KeyValuePair<string,object>("identifier",Identifier);
        if (Code is not null) yield return new KeyValuePair<string,object>("code",Code);
        if (Period is not null) yield return new KeyValuePair<string,object>("period",Period);
        if (Issuer is not null) yield return new KeyValuePair<string,object>("issuer",Issuer);
      }

    }

    /// <summary>
    /// An identifier for the person as this agent
    /// </summary>
    [FhirElement("identifier", InSummary=true, Order=90, FiveWs="FiveWs.identifier")]
    [Cardinality(Min=0,Max=-1)]
    [DataMember]
    public List<Hl7.Fhir.Model.Identifier> Identifier
    {
      get { if(_Identifier==null) _Identifier = new List<Hl7.Fhir.Model.Identifier>(); return _Identifier; }
      set { _Identifier = value; OnPropertyChanged("Identifier"); }
    }

    private List<Hl7.Fhir.Model.Identifier> _Identifier;

    /// <summary>
    /// Whether this practitioner's record is in active use
    /// </summary>
    [FhirElement("active", InSummary=true, Order=100, FiveWs="FiveWs.status")]
    [DataMember]
    public Hl7.Fhir.Model.FhirBoolean ActiveElement
    {
      get { return _ActiveElement; }
      set { _ActiveElement = value; OnPropertyChanged("ActiveElement"); }
    }

    private Hl7.Fhir.Model.FhirBoolean _ActiveElement;

    /// <summary>
    /// Whether this practitioner's record is in active use
    /// </summary>
    /// <remarks>This uses the native .NET datatype, rather than the FHIR equivalent</remarks>
    [IgnoreDataMember]
    public bool? Active
    {
      get { return ActiveElement != null ? ActiveElement.Value : null; }
      set
      {
        if (value == null)
          ActiveElement = null;
        else
          ActiveElement = new Hl7.Fhir.Model.FhirBoolean(value);
        OnPropertyChanged("Active");
      }
    }

    /// <summary>
    /// The name(s) associated with the practitioner
    /// </summary>
    [FhirElement("name", InSummary=true, Order=110)]
    [Cardinality(Min=0,Max=-1)]
    [DataMember]
    public List<Hl7.Fhir.Model.HumanName> Name
    {
      get { if(_Name==null) _Name = new List<Hl7.Fhir.Model.HumanName>(); return _Name; }
      set { _Name = value; OnPropertyChanged("Name"); }
    }

    private List<Hl7.Fhir.Model.HumanName> _Name;

    /// <summary>
    /// A contact detail for the practitioner (that apply to all roles)
    /// </summary>
    [FhirElement("telecom", InSummary=true, Order=120)]
    [Cardinality(Min=0,Max=-1)]
    [DataMember]
    public List<Hl7.Fhir.Model.ContactPoint> Telecom
    {
      get { if(_Telecom==null) _Telecom = new List<Hl7.Fhir.Model.ContactPoint>(); return _Telecom; }
      set { _Telecom = value; OnPropertyChanged("Telecom"); }
    }

    private List<Hl7.Fhir.Model.ContactPoint> _Telecom;

    /// <summary>
    /// Address(es) of the practitioner that are not role specific (typically home address)
    /// </summary>
    [FhirElement("address", InSummary=true, Order=130)]
    [Cardinality(Min=0,Max=-1)]
    [DataMember]
    public List<Hl7.Fhir.Model.Address> Address
    {
      get { if(_Address==null) _Address = new List<Hl7.Fhir.Model.Address>(); return _Address; }
      set { _Address = value; OnPropertyChanged("Address"); }
    }

    private List<Hl7.Fhir.Model.Address> _Address;

    /// <summary>
    /// male | female | other | unknown
    /// </summary>
    [FhirElement("gender", InSummary=true, Order=140)]
    [DeclaredType(Type = typeof(Code))]
    [Binding("AdministrativeGender")]
    [DataMember]
    public Code<Hl7.Fhir.Model.AdministrativeGender> GenderElement
    {
      get { return _GenderElement; }
      set { _GenderElement = value; OnPropertyChanged("GenderElement"); }
    }

    private Code<Hl7.Fhir.Model.AdministrativeGender> _GenderElement;

    /// <summary>
    /// male | female | other | unknown
    /// </summary>
    /// <remarks>This uses the native .NET datatype, rather than the FHIR equivalent</remarks>
    [IgnoreDataMember]
    public Hl7.Fhir.Model.AdministrativeGender? Gender
    {
      get { return GenderElement != null ? GenderElement.Value : null; }
      set
      {
        if (value == null)
          GenderElement = null;
        else
          GenderElement = new Code<Hl7.Fhir.Model.AdministrativeGender>(value);
        OnPropertyChanged("Gender");
      }
    }

    /// <summary>
    /// The date  on which the practitioner was born
    /// </summary>
    [FhirElement("birthDate", InSummary=true, Order=150)]
    [DataMember]
    public Hl7.Fhir.Model.Date BirthDateElement
    {
      get { return _BirthDateElement; }
      set { _BirthDateElement = value; OnPropertyChanged("BirthDateElement"); }
    }

    private Hl7.Fhir.Model.Date _BirthDateElement;

    /// <summary>
    /// The date  on which the practitioner was born
    /// </summary>
    /// <remarks>This uses the native .NET datatype, rather than the FHIR equivalent</remarks>
    [IgnoreDataMember]
    public string BirthDate
    {
      get { return BirthDateElement != null ? BirthDateElement.Value : null; }
      set
      {
        if (value == null)
          BirthDateElement = null;
        else
          BirthDateElement = new Hl7.Fhir.Model.Date(value);
        OnPropertyChanged("BirthDate");
      }
    }

    /// <summary>
    /// Image of the person
    /// </summary>
    [FhirElement("photo", Order=160)]
    [Cardinality(Min=0,Max=-1)]
    [DataMember]
    public List<Hl7.Fhir.Model.Attachment> Photo
    {
      get { if(_Photo==null) _Photo = new List<Hl7.Fhir.Model.Attachment>(); return _Photo; }
      set { _Photo = value; OnPropertyChanged("Photo"); }
    }

    private List<Hl7.Fhir.Model.Attachment> _Photo;

    /// <summary>
    /// Certification, licenses, or training pertaining to the provision of care
    /// </summary>
    [FhirElement("qualification", Order=170)]
    [Cardinality(Min=0,Max=-1)]
    [DataMember]
    public List<Hl7.Fhir.Model.Practitioner.QualificationComponent> Qualification
    {
      get { if(_Qualification==null) _Qualification = new List<Hl7.Fhir.Model.Practitioner.QualificationComponent>(); return _Qualification; }
      set { _Qualification = value; OnPropertyChanged("Qualification"); }
    }

    private List<Hl7.Fhir.Model.Practitioner.QualificationComponent> _Qualification;

    /// <summary>
    /// A language the practitioner can use in patient communication
    /// </summary>
    [FhirElement("communication", Order=180)]
    [Binding("Language")]
    [Cardinality(Min=0,Max=-1)]
    [DataMember]
    public List<Hl7.Fhir.Model.CodeableConcept> Communication
    {
      get { if(_Communication==null) _Communication = new List<Hl7.Fhir.Model.CodeableConcept>(); return _Communication; }
      set { _Communication = value; OnPropertyChanged("Communication"); }
    }

    private List<Hl7.Fhir.Model.CodeableConcept> _Communication;

    List<Identifier> IIdentifiable<List<Identifier>>.Identifier { get => Identifier; set => Identifier = value; }

    public override IDeepCopyable CopyTo(IDeepCopyable other)
    {
      var dest = other as Practitioner;

      if (dest == null)
      {
        throw new ArgumentException("Can only copy to an object of the same type", "other");
      }

      base.CopyTo(dest);
      if(Identifier != null) dest.Identifier = new List<Hl7.Fhir.Model.Identifier>(Identifier.DeepCopy());
      if(ActiveElement != null) dest.ActiveElement = (Hl7.Fhir.Model.FhirBoolean)ActiveElement.DeepCopy();
      if(Name != null) dest.Name = new List<Hl7.Fhir.Model.HumanName>(Name.DeepCopy());
      if(Telecom != null) dest.Telecom = new List<Hl7.Fhir.Model.ContactPoint>(Telecom.DeepCopy());
      if(Address != null) dest.Address = new List<Hl7.Fhir.Model.Address>(Address.DeepCopy());
      if(GenderElement != null) dest.GenderElement = (Code<Hl7.Fhir.Model.AdministrativeGender>)GenderElement.DeepCopy();
      if(BirthDateElement != null) dest.BirthDateElement = (Hl7.Fhir.Model.Date)BirthDateElement.DeepCopy();
      if(Photo != null) dest.Photo = new List<Hl7.Fhir.Model.Attachment>(Photo.DeepCopy());
      if(Qualification != null) dest.Qualification = new List<Hl7.Fhir.Model.Practitioner.QualificationComponent>(Qualification.DeepCopy());
      if(Communication != null) dest.Communication = new List<Hl7.Fhir.Model.CodeableConcept>(Communication.DeepCopy());
      return dest;
    }

    public override IDeepCopyable DeepCopy()
    {
      return CopyTo(new Practitioner());
    }

    ///<inheritdoc />
    public override bool Matches(IDeepComparable other)
    {
      var otherT = other as Practitioner;
      if(otherT == null) return false;

      if(!base.Matches(otherT)) return false;
      if( !DeepComparable.Matches(Identifier, otherT.Identifier)) return false;
      if( !DeepComparable.Matches(ActiveElement, otherT.ActiveElement)) return false;
      if( !DeepComparable.Matches(Name, otherT.Name)) return false;
      if( !DeepComparable.Matches(Telecom, otherT.Telecom)) return false;
      if( !DeepComparable.Matches(Address, otherT.Address)) return false;
      if( !DeepComparable.Matches(GenderElement, otherT.GenderElement)) return false;
      if( !DeepComparable.Matches(BirthDateElement, otherT.BirthDateElement)) return false;
      if( !DeepComparable.Matches(Photo, otherT.Photo)) return false;
      if( !DeepComparable.Matches(Qualification, otherT.Qualification)) return false;
      if( !DeepComparable.Matches(Communication, otherT.Communication)) return false;

      return true;
    }

    public override bool IsExactly(IDeepComparable other)
    {
      var otherT = other as Practitioner;
      if(otherT == null) return false;

      if(!base.IsExactly(otherT)) return false;
      if( !DeepComparable.IsExactly(Identifier, otherT.Identifier)) return false;
      if( !DeepComparable.IsExactly(ActiveElement, otherT.ActiveElement)) return false;
      if( !DeepComparable.IsExactly(Name, otherT.Name)) return false;
      if( !DeepComparable.IsExactly(Telecom, otherT.Telecom)) return false;
      if( !DeepComparable.IsExactly(Address, otherT.Address)) return false;
      if( !DeepComparable.IsExactly(GenderElement, otherT.GenderElement)) return false;
      if( !DeepComparable.IsExactly(BirthDateElement, otherT.BirthDateElement)) return false;
      if( !DeepComparable.IsExactly(Photo, otherT.Photo)) return false;
      if( !DeepComparable.IsExactly(Qualification, otherT.Qualification)) return false;
      if( !DeepComparable.IsExactly(Communication, otherT.Communication)) return false;

      return true;
    }

    [IgnoreDataMember]
    public override IEnumerable<Base> Children
    {
      get
      {
        foreach (var item in base.Children) yield return item;
        foreach (var elem in Identifier) { if (elem != null) yield return elem; }
        if (ActiveElement != null) yield return ActiveElement;
        foreach (var elem in Name) { if (elem != null) yield return elem; }
        foreach (var elem in Telecom) { if (elem != null) yield return elem; }
        foreach (var elem in Address) { if (elem != null) yield return elem; }
        if (GenderElement != null) yield return GenderElement;
        if (BirthDateElement != null) yield return BirthDateElement;
        foreach (var elem in Photo) { if (elem != null) yield return elem; }
        foreach (var elem in Qualification) { if (elem != null) yield return elem; }
        foreach (var elem in Communication) { if (elem != null) yield return elem; }
      }
    }

    [IgnoreDataMember]
    public override IEnumerable<ElementValue> NamedChildren
    {
      get
      {
        foreach (var item in base.NamedChildren) yield return item;
        foreach (var elem in Identifier) { if (elem != null) yield return new ElementValue("identifier", elem); }
        if (ActiveElement != null) yield return new ElementValue("active", ActiveElement);
        foreach (var elem in Name) { if (elem != null) yield return new ElementValue("name", elem); }
        foreach (var elem in Telecom) { if (elem != null) yield return new ElementValue("telecom", elem); }
        foreach (var elem in Address) { if (elem != null) yield return new ElementValue("address", elem); }
        if (GenderElement != null) yield return new ElementValue("gender", GenderElement);
        if (BirthDateElement != null) yield return new ElementValue("birthDate", BirthDateElement);
        foreach (var elem in Photo) { if (elem != null) yield return new ElementValue("photo", elem); }
        foreach (var elem in Qualification) { if (elem != null) yield return new ElementValue("qualification", elem); }
        foreach (var elem in Communication) { if (elem != null) yield return new ElementValue("communication", elem); }
      }
    }

    protected override bool TryGetValue(string key, out object value)
    {
      switch (key)
      {
        case "identifier":
          value = Identifier;
          return Identifier?.Any() == true;
        case "active":
          value = ActiveElement;
          return ActiveElement is not null;
        case "name":
          value = Name;
          return Name?.Any() == true;
        case "telecom":
          value = Telecom;
          return Telecom?.Any() == true;
        case "address":
          value = Address;
          return Address?.Any() == true;
        case "gender":
          value = GenderElement;
          return GenderElement is not null;
        case "birthDate":
          value = BirthDateElement;
          return BirthDateElement is not null;
        case "photo":
          value = Photo;
          return Photo?.Any() == true;
        case "qualification":
          value = Qualification;
          return Qualification?.Any() == true;
        case "communication":
          value = Communication;
          return Communication?.Any() == true;
        default:
          return base.TryGetValue(key, out value);
      }

    }

    protected override IEnumerable<KeyValuePair<string, object>> GetElementPairs()
    {
      foreach (var kvp in base.GetElementPairs()) yield return kvp;
      if (Identifier?.Any() == true) yield return new KeyValuePair<string,object>("identifier",Identifier);
      if (ActiveElement is not null) yield return new KeyValuePair<string,object>("active",ActiveElement);
      if (Name?.Any() == true) yield return new KeyValuePair<string,object>("name",Name);
      if (Telecom?.Any() == true) yield return new KeyValuePair<string,object>("telecom",Telecom);
      if (Address?.Any() == true) yield return new KeyValuePair<string,object>("address",Address);
      if (GenderElement is not null) yield return new KeyValuePair<string,object>("gender",GenderElement);
      if (BirthDateElement is not null) yield return new KeyValuePair<string,object>("birthDate",BirthDateElement);
      if (Photo?.Any() == true) yield return new KeyValuePair<string,object>("photo",Photo);
      if (Qualification?.Any() == true) yield return new KeyValuePair<string,object>("qualification",Qualification);
      if (Communication?.Any() == true) yield return new KeyValuePair<string,object>("communication",Communication);
    }

  }

}

// end of file
