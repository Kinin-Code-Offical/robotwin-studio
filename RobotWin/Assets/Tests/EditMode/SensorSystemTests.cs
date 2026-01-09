// Sensor System Comprehensive Tests
// Tests ADC, digital inputs, ultrasonic, temperature sensors

using NUnit.Framework;
using RobotTwin.CoreSim;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.Tests.EditMode
{
    [TestFixture]
    public class SensorSystemTests
    {
        private const float Epsilon = 0.001f;

        [Test]
        public void ADC_ReadsCorrectVoltage()
        {
            // Arrange
            var signal = new SignalSpec
            {
                Id = "TestSignal",
                Type = SignalType.Voltage,
                Value = 3.3f
            };

            // Act
            float adcValue = ConvertVoltageToADC(signal.Value, 10); // 10-bit ADC

            // Assert
            Assert.That(adcValue, Is.EqualTo(1023).Within(1));
        }

        [Test]
        public void ADC_10Bit_RangeCheck()
        {
            // Test 0V
            float adc0 = ConvertVoltageToADC(0.0f, 10);
            Assert.That(adc0, Is.EqualTo(0).Within(Epsilon));

            // Test 1.65V (half)
            float adcHalf = ConvertVoltageToADC(1.65f, 10);
            Assert.That(adcHalf, Is.EqualTo(511).Within(2));

            // Test 3.3V (full scale)
            float adcFull = ConvertVoltageToADC(3.3f, 10);
            Assert.That(adcFull, Is.EqualTo(1023).Within(1));
        }

        [Test]
        public void ADC_12Bit_HigherResolution()
        {
            // 12-bit ADC: 0-4095
            float adcFull = ConvertVoltageToADC(3.3f, 12);
            Assert.That(adcFull, Is.EqualTo(4095).Within(1));

            // Quarter scale
            float adcQuarter = ConvertVoltageToADC(0.825f, 12);
            Assert.That(adcQuarter, Is.EqualTo(1023).Within(2));
        }

        [Test]
        public void DigitalInput_HighThreshold()
        {
            // Arrange
            float highVoltage = 3.0f;
            float threshold = 2.5f;

            // Act
            bool isHigh = highVoltage > threshold;

            // Assert
            Assert.IsTrue(isHigh);
        }

        [Test]
        public void DigitalInput_LowThreshold()
        {
            // Arrange
            float lowVoltage = 0.5f;
            float threshold = 2.5f;

            // Act
            bool isLow = lowVoltage < threshold;

            // Assert
            Assert.IsTrue(isLow);
        }

        [Test]
        public void DigitalInput_Hysteresis()
        {
            // Arrange
            float upperThreshold = 2.5f;
            float lowerThreshold = 1.5f;
            bool currentState = false;

            // Act & Assert - Rising edge
            float voltage = 2.6f;
            currentState = ApplyHysteresis(voltage, currentState, lowerThreshold, upperThreshold);
            Assert.IsTrue(currentState);

            // Stays high above lower threshold
            voltage = 2.0f;
            currentState = ApplyHysteresis(voltage, currentState, lowerThreshold, upperThreshold);
            Assert.IsTrue(currentState);

            // Falls below lower threshold
            voltage = 1.4f;
            currentState = ApplyHysteresis(voltage, currentState, lowerThreshold, upperThreshold);
            Assert.IsFalse(currentState);
        }

        [Test]
        public void UltrasonicSensor_DistanceCalculation()
        {
            // Arrange
            float pulseWidthMicroseconds = 1000.0f; // 1ms
            float speedOfSound = 343.0f; // m/s at 20°C

            // Act
            float distance = CalculateUltrasonicDistance(pulseWidthMicroseconds, speedOfSound);

            // Assert
            // Distance = (time * speed) / 2
            // 0.001s * 343m/s / 2 = 0.1715m = 171.5mm
            Assert.That(distance, Is.EqualTo(0.1715f).Within(0.001f));
        }

        [Test]
        public void UltrasonicSensor_MinMaxRange()
        {
            float speedOfSound = 343.0f;

            // Min range (20us pulse)
            float minDistance = CalculateUltrasonicDistance(20.0f, speedOfSound);
            Assert.That(minDistance, Is.GreaterThan(0.0f));

            // Max range (typical ~4m = 23ms pulse)
            float maxDistance = CalculateUltrasonicDistance(23000.0f, speedOfSound);
            Assert.That(maxDistance, Is.LessThan(5.0f));
        }

        [Test]
        public void TemperatureSensor_NTCResistance()
        {
            // Arrange
            float temperature = 25.0f; // °C
            float r25 = 10000.0f; // 10kΩ at 25°C
            float beta = 3950.0f;

            // Act
            float resistance = CalculateNTCResistance(temperature, r25, beta);

            // Assert
            Assert.That(resistance, Is.EqualTo(r25).Within(100));

            // At 50°C, resistance should decrease significantly
            float resistance50 = CalculateNTCResistance(50.0f, r25, beta);
            Assert.That(resistance50, Is.LessThan(r25 * 0.5f));
        }

        [Test]
        public void TemperatureSensor_VoltageDivider()
        {
            // Arrange
            float vcc = 5.0f;
            float r1 = 10000.0f; // Series resistor
            float r2 = 10000.0f; // NTC at 25°C

            // Act
            float vout = CalculateVoltageDivider(vcc, r1, r2);

            // Assert
            Assert.That(vout, Is.EqualTo(2.5f).Within(0.01f));
        }

        [Test]
        public void PWM_DutyCycleToVoltage()
        {
            // Arrange
            float vcc = 5.0f;
            int dutyCycle = 127; // 50% (0-255)

            // Act
            float voltage = ConvertPWMToVoltage(dutyCycle, 255, vcc);

            // Assert
            Assert.That(voltage, Is.EqualTo(2.5f).Within(0.05f));
        }

        [Test]
        public void PWM_ExtremeValues()
        {
            float vcc = 3.3f;

            // 0% duty cycle
            float v0 = ConvertPWMToVoltage(0, 255, vcc);
            Assert.That(v0, Is.EqualTo(0.0f).Within(Epsilon));

            // 100% duty cycle
            float v100 = ConvertPWMToVoltage(255, 255, vcc);
            Assert.That(v100, Is.EqualTo(3.3f).Within(0.01f));
        }

        [Test]
        public void AnalogComparator_BasicOperation()
        {
            // Act
            bool result1 = AnalogCompare(2.5f, 2.0f);
            bool result2 = AnalogCompare(1.5f, 2.0f);

            // Assert
            Assert.IsTrue(result1); // 2.5V > 2.0V
            Assert.IsFalse(result2); // 1.5V < 2.0V
        }

        [Test]
        public void PullupResistor_OpenDrainCalculation()
        {
            // Arrange
            float vcc = 5.0f;
            float pullupResistance = 10000.0f;
            float sinkCurrent = 0.002f; // 2mA when low

            // Act - When pin is floating (open)
            float vFloating = vcc; // Pulled up
            Assert.That(vFloating, Is.EqualTo(5.0f));

            // Act - When pin sinks current
            float vSinking = vcc - (sinkCurrent * pullupResistance);
            Assert.That(vSinking, Is.LessThan(1.0f)); // Should be near ground
        }

        [Test]
        public void Debounce_ButtonPress()
        {
            // Arrange
            var buttonStates = new List<bool>
            {
                false, true, false, true, true, true, true, false, true, true
            };
            int debounceCount = 3;

            // Act
            bool stableState = DebounceSignal(buttonStates, debounceCount);

            // Assert
            Assert.IsTrue(stableState); // Majority is true
        }

        [Test]
        public void ADC_NoiseFiltering_MovingAverage()
        {
            // Arrange
            var readings = new float[] { 512, 515, 510, 513, 511, 514, 509 };

            // Act
            float filtered = MovingAverage(readings);

            // Assert
            Assert.That(filtered, Is.EqualTo(512).Within(2));
        }

        [Test]
        public void Sensor_OffsetCalibration()
        {
            // Arrange
            float rawReading = 520.0f;
            float calibrationOffset = -8.0f;

            // Act
            float calibrated = rawReading + calibrationOffset;

            // Assert
            Assert.That(calibrated, Is.EqualTo(512.0f).Within(Epsilon));
        }

        [Test]
        public void Sensor_ScalingFactor()
        {
            // Arrange - 10mV per unit sensor
            float rawReading = 250.0f;
            float scalingFactor = 0.01f; // 10mV

            // Act
            float voltage = rawReading * scalingFactor;

            // Assert
            Assert.That(voltage, Is.EqualTo(2.5f).Within(Epsilon));
        }

        [Test]
        public void Photoresistor_LightIntensity()
        {
            // Arrange
            float vcc = 5.0f;
            float seriesResistor = 10000.0f;
            float ldrBright = 1000.0f; // 1kΩ in bright light
            float ldrDark = 100000.0f; // 100kΩ in darkness

            // Act
            float vBright = CalculateVoltageDivider(vcc, seriesResistor, ldrBright);
            float vDark = CalculateVoltageDivider(vcc, seriesResistor, ldrDark);

            // Assert
            Assert.That(vBright, Is.LessThan(1.0f)); // Low voltage in bright light
            Assert.That(vDark, Is.GreaterThan(4.0f)); // High voltage in darkness
        }

        [Test]
        public void SensorArray_Multiplexing()
        {
            // Arrange - 8 sensors on 3-bit multiplexer
            var sensorValues = new float[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f, 7.0f, 8.0f };

            // Act & Assert
            for (int channel = 0; channel < 8; channel++)
            {
                float value = SelectMuxChannel(sensorValues, channel);
                Assert.That(value, Is.EqualTo(sensorValues[channel]));
            }
        }

        [Test]
        public void Accelerometer_GravityReading()
        {
            // Arrange - 1g = 1024 ADC counts (typical)
            float adcReading = 1024.0f;
            float sensitivity = 1024.0f; // counts per g

            // Act
            float acceleration = adcReading / sensitivity;

            // Assert
            Assert.That(acceleration, Is.EqualTo(1.0f).Within(0.01f));
        }

        // Helper Methods
        private float ConvertVoltageToADC(float voltage, int bits)
        {
            float vref = 3.3f;
            float maxValue = (1 << bits) - 1;
            return Math.Min(maxValue, (voltage / vref) * maxValue);
        }

        private bool ApplyHysteresis(float value, bool currentState, float lowerThreshold, float upperThreshold)
        {
            if (!currentState && value > upperThreshold)
                return true;
            if (currentState && value < lowerThreshold)
                return false;
            return currentState;
        }

        private float CalculateUltrasonicDistance(float pulseWidthUs, float speedOfSound)
        {
            float pulseWidthS = pulseWidthUs / 1000000.0f;
            return (pulseWidthS * speedOfSound) / 2.0f;
        }

        private float CalculateNTCResistance(float tempC, float r25, float beta)
        {
            float t0 = 25.0f + 273.15f;
            float t = tempC + 273.15f;
            return r25 * (float)Math.Exp(beta * (1.0f / t - 1.0f / t0));
        }

        private float CalculateVoltageDivider(float vin, float r1, float r2)
        {
            return vin * (r2 / (r1 + r2));
        }

        private float ConvertPWMToVoltage(int dutyCycle, int maxDuty, float vcc)
        {
            return (dutyCycle / (float)maxDuty) * vcc;
        }

        private bool AnalogCompare(float input, float reference)
        {
            return input > reference;
        }

        private bool DebounceSignal(List<bool> states, int requiredCount)
        {
            int trueCount = states.Count(s => s);
            return trueCount >= requiredCount;
        }

        private float MovingAverage(float[] readings)
        {
            return readings.Average();
        }

        private float SelectMuxChannel(float[] sensors, int channel)
        {
            return sensors[channel];
        }
    }
}
