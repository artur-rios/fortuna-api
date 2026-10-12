#include "../include/fortuna_core.h"

int main(void) {
    char *response = NULL;
    int status = fortuna_capabilities("{}", &response);
    if (response != NULL) {
        fortuna_string_free(response);
    }
    if (status != FORTUNA_STATUS_OK) {
        return 1;
    }

    /* An export the core does not implement answers 501 without initialization. */
    response = NULL;
    status = fortuna_api_transfers_post("{}", &response);
    if (response != NULL) {
        fortuna_string_free(response);
    }
    return status == FORTUNA_STATUS_NOT_IMPLEMENTED ? 0 : 1;
}
