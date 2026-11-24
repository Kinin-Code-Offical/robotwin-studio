// Two-joint servo arm without libraries: 50 Hz pulse trains on D9 (shoulder)
// and D10 (elbow). The shoulder sweeps 0..180 degrees in 2-degree steps; the
// elbow follows the same way over half the range (45..135), so the arm curls
// and stretches like a real two-joint arm. Status goes to the serial port.
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

int angle = 0, step = 2;

void loop() {
  pulse(SERVO_A, angleToUs(angle));
  pulse(SERVO_B, angleToUs(45 + angle / 2));
  delay(16);
  angle += step;
  if (angle >= 180 || angle <= 0) {
    step = -step;
    Serial.print("Aci: ");
    Serial.println(angle);
  }
}
