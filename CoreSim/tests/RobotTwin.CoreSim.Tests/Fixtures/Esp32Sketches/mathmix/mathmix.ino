// Everyday maths on the real chip: float trigonometry, powers and logs, double division, float printing.
volatile float fa = 1.25f, fb = 0.75f;
volatile double da = 10.0, db = 3.0;

void setup() {
  Serial.begin(115200);
  float s = sinf(fa), c = cosf(fb), p = powf(fa, 3.5f), t = atan2f(fa, fb), e = expf(fb), l = logf(fa);
  double q = da / db, r = sqrt(da);
  Serial.printf("s=%.6f c=%.6f p=%.6f t=%.6f e=%.6f l=%.6f\n", s, c, p, t, e, l);
  Serial.printf("q=%.12f r=%.12f\n", q, r);
  Serial.println(String(fa * fb, 4));
  Serial.println("mix hazir");
}

void loop() {}
