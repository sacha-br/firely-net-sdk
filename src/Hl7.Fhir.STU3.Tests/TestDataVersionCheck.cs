﻿using Hl7.Fhir.Model;
using Hl7.Fhir.Tests;
using Hl7.Fhir.Utility;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Tasks = System.Threading.Tasks;

namespace Hl7.Fhir.Tests
{
    /// <summary>
    /// All this is to do is read all the unit test data to ensure that they are all compatible with STU3
    /// (By just trying to de-serialize all the content)
    /// </summary>
    [TestClass]
    public class TestDataVersionCheck
    {
        [TestMethod]   // not everything parses correctly
        public async Tasks.Task VerifyAllTestData()
        {
            string location = typeof(TestDataHelper).GetTypeInfo().Assembly.Location;
            var path = Path.GetDirectoryName(location) + "/TestData";
            Console.WriteLine(path);
            StringBuilder issues = new StringBuilder();
            await ValidateFolder(path, path, issues);
            Console.Write(issues.ToString());
            Assert.AreEqual("", issues.ToString());
        }

        private async Tasks.Task ValidateFolder(string basePath, string path, StringBuilder issues)
        {
            var xmlParser = new Fhir.Serialization.FhirXmlParser();
            var jsonParser = new Fhir.Serialization.FhirJsonParser();
            Console.WriteLine($"Validating test files in {path.Replace(basePath, "")}");
            foreach (var item in Directory.EnumerateFiles(path))
            {
                try
                {
                    if (item.EndsWith(".dll"))
                        continue;
                    if (item.EndsWith(".exe"))
                        continue;
                    if (item.EndsWith(".pdb"))
                        continue;
                    string content = File.ReadAllText(item);
                    if (new FileInfo(item).Extension == ".xml")
                    {
                        Console.WriteLine($"    {item.Replace(path+"//", "")}");
                        await xmlParser.ParseAsync<Resource>(content);
                    }
                    else if (new FileInfo(item).Extension == ".json")
                    {
                        Console.WriteLine($"    {item.Replace(path + "//", "")}");
                        await jsonParser.ParseAsync<Resource>(content);
                    }
                    else
                    {
                        Console.WriteLine($"    {item} (unknown content)");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    {item} (parse error)");
                    Console.WriteLine($"        --> {ex.Message}");
                    issues.AppendLine($"    {item} (parse error) --> {ex.Message}");
                }
            }
            foreach (var item in Directory.EnumerateDirectories(path))
            {
                await ValidateFolder(basePath, item, issues);
            }
        }
    }
}