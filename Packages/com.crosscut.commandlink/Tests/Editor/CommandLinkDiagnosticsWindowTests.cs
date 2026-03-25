using System.Linq;
using CrossCut.CommandLink.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CrossCut.CommandLink.Tests
{
    public sealed class CommandLinkDiagnosticsWindowTests
    {
        [Test]
        public void OpenCreatesDiagnosticsWindow()
        {
            CommandLinkDiagnosticsWindow.Open();

            var window = Resources.FindObjectsOfTypeAll<CommandLinkDiagnosticsWindow>().LastOrDefault();

            Assert.That(window, Is.Not.Null);

            window.Close();
        }
    }
}
