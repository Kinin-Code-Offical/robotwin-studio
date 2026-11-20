// Servo sweep without libraries: a 50 Hz pulse train on D9 (1.0-2.0 ms)
// sweeps the horn 0..180 degrees; D10 holds a second servo opposite.
// The pot on A0 (if wired) sets the speed. Status goes to the serial port.
const int SERVO_A = 9;
const int SERVO_B = 10;

void pulse(int pin, int us) {
  digitalWrite(pin, HIGH);
  delayMicroseconds(us);
  digitalWrite(pin, LOW);
}

int angleToUs(int angle) { return 544 + (long)angle * (2400 - 544) / 180; }

void setup() {
  pinMode(SERVO_A, OUTPUT);
  pinMode(SERVO_B, OUTPUT);
  Serial.begin(9600);
  Serial.println("Servo tarama basladi");
}

int angle = 0, step = 3;

void loop() {
  pulse(SERVO_A, angleToUs(angle));
  pulse(SERVO_B, angleToUs(180 - angle));
  delay(16);
  angle += step;
  if (angle >= 180 || angle <= 0) {
    step = -step;
    Serial.print("Aci: ");
    Serial.println(angle);
  }
}
