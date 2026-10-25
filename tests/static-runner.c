/* Test-only Wine stand-in for the scratch-container launch lifecycle. */
#include <stdio.h>
#include <sqlite3.h>

int main(void)
{
    sqlite3 *db = NULL;
    sqlite3_stmt *statement = NULL;
    int result = 42;
    if (sqlite3_open("/fixture/Client/Saved/LocalStorage/LocalStorage.db", &db) != SQLITE_OK) return 43;
    if (sqlite3_prepare_v2(db, "SELECT value FROM LocalStorage WHERE key='ShadowQuality'",
                           -1, &statement, NULL) == SQLITE_OK &&
        sqlite3_step(statement) == SQLITE_ROW) {
        const unsigned char *value = sqlite3_column_text(statement, 0);
        FILE *observed = fopen("/fixture/observed-shadow.txt", "w");
        if (observed != NULL) { fprintf(observed, "%s", value); fclose(observed); }
        if (value != NULL && value[0] == '3' && value[1] == '\0') result = 0;
    }
    sqlite3_finalize(statement);
    sqlite3_close(db);
    FILE *status = fopen("/fixture/state/wuwa-touch-launcher/status.json", "w");
    if (status == NULL) return 44;
    fprintf(status, "{\"phase\":\"game-exited\",\"filtering\":false,\"exitCode\":%d,\"targetCount\":0}", result);
    fclose(status);
    return result;
}
