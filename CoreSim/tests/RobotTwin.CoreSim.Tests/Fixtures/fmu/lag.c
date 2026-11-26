/* A first-order lag as an FMI 2.0 model-exchange FMU, written for RobotWin's tests: the importer integrates
   y' = (k u - y) / tau. Variables (value references): 0 u (input), 1 y (output, the state), 2 der(y), 3 k, 4 tau. */
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
typedef struct
{
    fmi2Boolean newDiscreteStatesNeeded;
    fmi2Boolean terminateSimulation;
    fmi2Boolean nominalsOfContinuousStatesChanged;
    fmi2Boolean valuesOfContinuousStatesChanged;
    fmi2Boolean nextEventTimeDefined;
    fmi2Real nextEventTime;
} fmi2EventInfo;

#define EXPORT __declspec(dllexport)
#define GUID "{5d2a9e47-1c3b-4f60-a2d4-7e1f00c0a9a9}"

typedef struct
{
    double u, y, k, tau;
    const fmi2CallbackFunctions *cb;
} Model;

EXPORT fmi2Component fmi2Instantiate(fmi2String name, int type, fmi2String guid, fmi2String resources,
                                     const fmi2CallbackFunctions *f, fmi2Boolean visible, fmi2Boolean logging)
{
    Model *m;
    if (type != 0 || guid == NULL || strcmp(guid, GUID) != 0) return NULL;
    m = (Model *)f->allocateMemory(1, sizeof(Model));
    if (m == NULL) return NULL;
    m->k = 1;
    m->tau = 0.01;
    m->cb = f;
    return m;
}

EXPORT void fmi2FreeInstance(fmi2Component c)
{
    Model *m = (Model *)c;
    if (m != NULL) m->cb->freeMemory(m);
}

EXPORT fmi2Status fmi2SetupExperiment(fmi2Component c, fmi2Boolean td, fmi2Real tol, fmi2Real start, fmi2Boolean sd, fmi2Real stop) { return fmi2OK; }
EXPORT fmi2Status fmi2EnterInitializationMode(fmi2Component c) { return fmi2OK; }
EXPORT fmi2Status fmi2ExitInitializationMode(fmi2Component c) { return fmi2OK; }
EXPORT fmi2Status fmi2Terminate(fmi2Component c) { return fmi2OK; }

EXPORT fmi2Status fmi2SetReal(fmi2Component c, const fmi2ValueReference vr[], size_t n, const fmi2Real v[])
{
    Model *m = (Model *)c;
    size_t i;
    for (i = 0; i < n; i++)
    {
        switch (vr[i])
        {
            case 0: m->u = v[i]; break;
            case 1: m->y = v[i]; break;
            case 3: m->k = v[i]; break;
            case 4:
                if (v[i] <= 0) return fmi2Error;
                m->tau = v[i];
                break;
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
            case 2: v[i] = (m->k * m->u - m->y) / m->tau; break;
            case 3: v[i] = m->k; break;
            case 4: v[i] = m->tau; break;
            default: return fmi2Error;
        }
    }
    return fmi2OK;
}

EXPORT fmi2Status fmi2SetInteger(fmi2Component c, const fmi2ValueReference vr[], size_t n, const fmi2Integer v[]) { return n == 0 ? fmi2OK : fmi2Error; }
EXPORT fmi2Status fmi2GetInteger(fmi2Component c, const fmi2ValueReference vr[], size_t n, fmi2Integer v[]) { return n == 0 ? fmi2OK : fmi2Error; }
EXPORT fmi2Status fmi2SetBoolean(fmi2Component c, const fmi2ValueReference vr[], size_t n, const fmi2Boolean v[]) { return n == 0 ? fmi2OK : fmi2Error; }
EXPORT fmi2Status fmi2GetBoolean(fmi2Component c, const fmi2ValueReference vr[], size_t n, fmi2Boolean v[]) { return n == 0 ? fmi2OK : fmi2Error; }
EXPORT fmi2Status fmi2SetTime(fmi2Component c, fmi2Real t) { return fmi2OK; }

EXPORT fmi2Status fmi2SetContinuousStates(fmi2Component c, const fmi2Real x[], size_t nx)
{
    if (nx != 1) return fmi2Error;
    ((Model *)c)->y = x[0];
    return fmi2OK;
}

EXPORT fmi2Status fmi2GetContinuousStates(fmi2Component c, fmi2Real x[], size_t nx)
{
    if (nx != 1) return fmi2Error;
    x[0] = ((Model *)c)->y;
    return fmi2OK;
}

EXPORT fmi2Status fmi2GetDerivatives(fmi2Component c, fmi2Real dx[], size_t nx)
{
    Model *m = (Model *)c;
    if (nx != 1) return fmi2Error;
    dx[0] = (m->k * m->u - m->y) / m->tau;
    return fmi2OK;
}

EXPORT fmi2Status fmi2GetEventIndicators(fmi2Component c, fmi2Real z[], size_t nz) { return nz == 0 ? fmi2OK : fmi2Error; }
EXPORT fmi2Status fmi2EnterEventMode(fmi2Component c) { return fmi2OK; }
EXPORT fmi2Status fmi2EnterContinuousTimeMode(fmi2Component c) { return fmi2OK; }

EXPORT fmi2Status fmi2NewDiscreteStates(fmi2Component c, fmi2EventInfo *info)
{
    memset(info, 0, sizeof *info);
    return fmi2OK;
}

EXPORT fmi2Status fmi2CompletedIntegratorStep(fmi2Component c, fmi2Boolean noSetPrior, fmi2Boolean *enterEventMode, fmi2Boolean *terminate)
{
    *enterEventMode = 0;
    *terminate = 0;
    return fmi2OK;
}
