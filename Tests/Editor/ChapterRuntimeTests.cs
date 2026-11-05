using System.IO;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace ChapterFlow.Tests
{
    public sealed class ChapterRuntimeTests
    {
        [Test]
        public void LifecycleStateSaveAndValidationContract()
        {
            var package = PackageInfo.FindForAssembly(typeof(ChapterDefinition).Assembly);
            var json = File.ReadAllText(Path.Combine(package.resolvedPath, "Samples~/QuickStart/signal-lab.json"));
            Assert.That(RuntimeChecks.Run(json), Is.GreaterThanOrEqualTo(45));
        }
    }
}
