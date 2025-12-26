using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RobotTwin.CoreSim.Catalogs;
using RobotTwin.CoreSim.Runtime;

namespace RobotTwin.Tests.PlayMode
{
    public class SmokeTest
    {
        [Test]
        public void BlinkyTemplate_LoadsCorrectly()
        {
            // Arrange
            var defaults = TemplateCatalog.GetDefaults();
            var blinky = defaults.Find(t => t.ID.Contains("exampletemplate-01"));

            // Assert
            Assert.IsNotNull(blinky, "Blinky template should exist in defaults");
            Assert.AreEqual("CircuitOnly", blinky.SystemType);
            Assert.IsNotNull(blinky.DefaultCircuit, "Blinky should have a default circuit");
            Assert.IsTrue(blinky.DefaultCircuit.Components.Count > 0, "Blinky should have components");
        }

        [Test]
        public void RunEngine_CanExecuteBlinky()
        {
            // Arrange
            var defaults = TemplateCatalog.GetDefaults();
            var blinky = defaults.Find(t => t.ID.Contains("exampletemplate-01"));
            Assert.IsNotNull(blinky, "Pre-check: Blinky must load");

            // Act
            var engine = new RunEngine(blinky.DefaultCircuit);
            engine.Step(); 
            engine.Step();

            // Assert
            Assert.IsTrue(engine.Session.TickIndex >= 2, "Engine should have advanced at least 2 ticks");
            Assert.IsTrue(engine.Session.TimeSeconds > 0, "Engine time should be > 0");
        }
    }
}
