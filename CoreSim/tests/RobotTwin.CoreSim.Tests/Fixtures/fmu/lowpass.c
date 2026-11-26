/* A first-order lag as an FMI 2.0 co-simulation FMU, written for RobotWin's tests: y' = (k*u - y) / tau, stepped
   exactly over each communication interval by its own solver. Variables (value references):
   0 u (input), 1 y (output), 2 k (parameter), 3 tau (parameter), 4 steps (Integer output), 5 saturated (Boolean
   output, |y| > limit), 6 limit (parameter). It allocates and logs through the importer's callbacks. */
#include <math.h>
#include <stddef.h>
#include <string.h>

typedef void *fmi2Component;
typedef void *fmi2ComponentEnvironment;
typedef unsigned int fmi2ValueReference;
typedef double fmi2Real;
typedef int fmi2Integer;
typedef int fmi2Boolean;
typedef const char *fmi2String;
typedef enum { fmi2OK, fmi2Warning, fmi2Discard, fmi2Error, fmi2Fatal, fmi2Pending } fmi2Status;
typedef void (*fmi2CallbackLogger)(fmi2ComponentEnvironment, fmi2String, fmi2Status, fmi2String, fmi2String, ...);
typedef void *(*fmi2CallbackAllocateMemory)(size_t, size_t);
typedef void (*fmi2CallbackFreeMemory)(void *);
typedef struct
{
    fmi2CallbackLogger logger;
    fmi2CallbackAllocateMemory allocateMemory;
    fmi2CallbackFreeMemory freeMemory;
    void *stepFinished;
    fmi2ComponentEnvironment componentEnvironment;
} fmi2CallbackFunctions;

#define EXPORT __declspec(dllexport)
#define GUID "{8c4e810f-3df3-4a00-8276-176fa3c9f000}"

typedef struct
{
    double u, y, k, tau, limit;
    int steps, saturated;
    const fmi2CallbackFunctions *cb;
} Model;

EXPORT fmi2Component fmi2Instantiate(fmi2String name, int type, fmi2String guid, fmi2String resources,
                                     const fmi2CallbackFunctions *f, fmi2Boolean visible, fmi2Boolean logging)
{
    Model *m;
    if (type != 1 || guid == NULL || strcmp(guid, GUID) != 0) return NULL;
    m = (Model *)f->allocateMemory(1, sizeof(Model));
    if (m == NULL) return NULL;
    m->k = 1;
    m->tau = 0.01;
    m->limit = 4.5;
    m->cb = f;
    f->logger(f->componentEnvironment, name, fmi2OK, "info", "lowpass ready");
    return m;
}

EXPORT fmi2Status fmi2SetupExperiment(fmi2Component c, fmi2Boolean td, fmi2Real tol, fmi2Real start, fmi2Boolean sd, fmi2Real stop) { return fmi2OK; }
EXPORT fmi2Status fmi2EnterInitializationMode(fmi2Component c) { return fmi2OK; }
EXPORT fmi2Status fmi2ExitInitializationMode(fmi2Component c) { return fmi2OK; }
EXPORT fmi2Status fmi2Terminate(fmi2Component c) { return fmi2OK; }

EXPORT void fmi2FreeInstance(fmi2Component c)
{
    Model *m = (Model *)c;
    if (m != NULL) m->cb->freeMemory(m);
}

EXPORT fmi2Status fmi2SetReal(fmi2Component c, const fmi2ValueReference vr[], size_t n, const fmi2Real v[])
{
    Model *m = (Model *)c;
    size_t i;
    for (i = 0; i < n; i++)
    {
        switch (vr[i])
        {
            case 0: m->u = v[i]; break;
            case 2: m->k = v[i]; break;
            case 3:
                if (v[i] <= 0) return fmi2Error;
                m->tau = v[i];
                break;
            case 6: m->limit = v[i]; break;
            default: return fmi2Error;
        }
    }

    return fmi2OK;
}

EXPORT fmi2Status fmi2GetReal(fmi2Component c, const fmi2ValueReference vr[], size_t n, fmi2Real v[])
{
    Model *m = (Model *)c;
    size_t i;
    for (i = 0; i < n; i++)
    {
        switch (vr[i])
        {
            case 0: v[i] = m->u; break;
            case 1: v[i] = m->y; break;
            case 2: v[i] = m->k; break;
            case 3: v[i] = m->tau; break;
            case 6: v[i] = m->limit; break;
            default: return fmi2Error;
        }
    }

    return fmi2OK;
}

EXPORT fmi2Status fmi2SetInteger(fmi2Component c, const fmi2ValueReference vr[], size_t n, const fmi2Integer v[]) { return n == 0 ? fmi2OK : fmi2Error; }

EXPORT fmi2Status fmi2GetInteger(fmi2Component c, const fmi2ValueReference vr[], size_t n, fmi2Integer v[])
{
    Model *m = (Model *)c;
    size_t i;
    for (i = 0; i < n; i++)
    {
        if (vr[i] != 4) return fmi2Error;
        v[i] = m->steps;
    }

    return fmi2OK;
}

EXPORT fmi2Status fmi2SetBoolean(fmi2Component c, const fmi2ValueReference vr[], size_t n, const fmi2Boolean v[]) { return n == 0 ? fmi2OK : fmi2Error; }

EXPORT fmi2Status fmi2GetBoolean(fmi2Component c, const fmi2ValueReference vr[], size_t n, fmi2Boolean v[])
{
    Model *m = (Model *)c;
    size_t i;
    for (i = 0; i < n; i++)
    {
        if (vr[i] != 5) return fmi2Error;
        v[i] = m->saturated;
    }

    return fmi2OK;
}

EXPORT fmi2Status fmi2DoStep(fmi2Component c, fmi2Real t, fmi2Real h, fmi2Boolean noSetPrior)
{
    Model *m = (Model *)c;
    double target = m->k * m->u;
    if (h < 0) return fmi2Error;
    m->y = target + (m->y - target) * exp(-h / m->tau);
    m->steps++;
    m->saturated = fabs(m->y) > m->limit;
    return fmi2OK;
}
