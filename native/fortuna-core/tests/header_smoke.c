#include "../include/fortuna_core.h"

int main(void) {
    char *response = NULL;
    int status = fortuna_capabilities("{}", &response);
    if (response != NULL) {
        fortuna_string_free(response);
    }
    return status == FORTUNA_STATUS_OK ? 0 : 1;
}
