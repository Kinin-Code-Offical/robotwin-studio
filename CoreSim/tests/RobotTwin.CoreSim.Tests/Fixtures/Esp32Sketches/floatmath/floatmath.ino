// The FPU's divide and square-root routines on the real chip: results printed bit for bit.
volatile float a = -7.0f, b = 3.0f, x = 2.0f;

void setup() {
  Serial.begin(115200);
  float q = a / b;
  float r = sqrtf(x);
  float s = 1.0f / b;
  uint32_t qb, rb, sb;
  memcpy(&qb, &q, 4);
  memcpy(&rb, &r, 4);
  memcpy(&sb, &s, 4);
  Serial.printf("q=%08lx r=%08lx s=%08lx\n", (unsigned long)qb, (unsigned long)rb, (unsigned long)sb);
  Serial.println("float hazir");
}

void loop() {}
