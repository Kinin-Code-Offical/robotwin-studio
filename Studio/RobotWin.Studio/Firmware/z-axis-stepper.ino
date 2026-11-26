// Z axis on a T8x8 lead screw: an A4988 (full step: MS1-MS3 left open) with DIR on D2, STEP on D3 and EN on D8.
// It lifts the carriage 40 mm and lowers it again with a trapezoidal speed ramp, as AccelStepper does: a stepper
// started at full speed stalls; ramped up at a steady acceleration it follows. No libraries.
// 200 full steps a turn, 8 mm a turn: 25 steps a millimetre.

const int DIR_PIN = 2;
const int STEP_PIN = 3;
const int EN_PIN = 8;
const long STEPS_PER_MM = 25;

#ifdef LANG_EN
#define MSG_READY "Z axis ready"
#define MSG_UP "Up 40 mm"
#define MSG_DOWN "Down 40 mm"
#define MSG_AT "Position (mm): "
#else
#define MSG_READY "Z ekseni hazir"
#define MSG_UP "Yukari 40 mm"
#define MSG_DOWN "Asagi 40 mm"
#define MSG_AT "Konum (mm): "
#endif

long position = 0;   // steps from where it started

// Moves a number of steps (positive: up) from a slow start to vmax and back down to a stop, at a steady acceleration
// (steps/s and steps/s^2): the speed after each step is v + a/v, and it slows once the steps left are v^2 / 2a.
void moveSteps(long steps, float vmax, float accel) {
  // A right-hand screw turned with DIR LOW lifts its nut.
  digitalWrite(DIR_PIN, steps > 0 ? LOW : HIGH);
  delayMicroseconds(5);
  long n = steps > 0 ? steps : -steps;
  const float vmin = 100.0;
  float v = vmin;
  for (long i = 0; i < n; i++) {
    long left = n - i;
    if (left <= (long)(v * v / (2.0 * accel))) {
      v -= accel / v;
      if (v < vmin) v = vmin;
    } else if (v < vmax) {
      v += accel / v;
      if (v > vmax) v = vmax;
    }

    unsigned long period = (unsigned long)(1000000.0 / v);
    digitalWrite(STEP_PIN, HIGH);
    delayMicroseconds(2);
    digitalWrite(STEP_PIN, LOW);
    delayMicroseconds(period > 60 ? period - 60 : 1);   // the arithmetic above takes about that long
    position += steps > 0 ? 1 : -1;
  }
}

void setup() {
  pinMode(DIR_PIN, OUTPUT);
  pinMode(STEP_PIN, OUTPUT);
  pinMode(EN_PIN, OUTPUT);
  digitalWrite(EN_PIN, LOW);   // the A4988 runs while EN is LOW
  Serial.begin(9600);
  Serial.println(MSG_READY);
  delay(200);
}

void loop() {
  Serial.println(MSG_UP);
  moveSteps(40 * STEPS_PER_MM, 800, 3000);
  Serial.print(MSG_AT);
  Serial.println(position / STEPS_PER_MM);
  delay(400);
  Serial.println(MSG_DOWN);
  moveSteps(-40 * STEPS_PER_MM, 800, 3000);
  Serial.print(MSG_AT);
  Serial.println(position / STEPS_PER_MM);
  delay(400);
}
