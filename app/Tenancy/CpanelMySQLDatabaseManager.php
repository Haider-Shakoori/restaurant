<?php

namespace App\Tenancy;

use RuntimeException;
use Stancl\Tenancy\Contracts\TenantWithDatabase;
use Stancl\Tenancy\TenantDatabaseManagers\MySQLDatabaseManager;
use Symfony\Component\Process\Process;

class CpanelMySQLDatabaseManager extends MySQLDatabaseManager
{
    public function createDatabase(TenantWithDatabase $tenant): bool
    {
        if (! config('platform.provisioning.cpanel.enabled')) {
            return parent::createDatabase($tenant);
        }

        $database = $tenant->database()->getName();

        $this->uapi('create_database', ['name' => $database]);

        try {
            $this->uapi('set_privileges_on_database', [
                'user' => (string) config('platform.provisioning.cpanel.database_user'),
                'database' => $database,
                'privileges' => 'ALL PRIVILEGES',
            ]);
        } catch (\Throwable $e) {
            try {
                $this->uapi('delete_database', ['name' => $database]);
            } catch (\Throwable) {
                // Best-effort cleanup. Preserve the original grant failure.
            }

            throw $e;
        }

        return true;
    }

    public function deleteDatabase(TenantWithDatabase $tenant): bool
    {
        if (! config('platform.provisioning.cpanel.enabled')) {
            return parent::deleteDatabase($tenant);
        }

        if (! $this->databaseExists($tenant->database()->getName())) {
            return true;
        }

        $this->uapi('delete_database', ['name' => $tenant->database()->getName()]);

        return true;
    }

    public function databaseExists(string $name): bool
    {
        if (! config('platform.provisioning.cpanel.enabled')) {
            return parent::databaseExists($name);
        }

        $data = $this->uapi('list_databases');

        foreach ($data as $database) {
            if (($database['database'] ?? null) === $name) {
                return true;
            }
        }

        return false;
    }

    /**
     * @return array<int, mixed>|array<string, mixed>
     */
    private function uapi(string $function, array $parameters = []): array
    {
        $binary = (string) config('platform.provisioning.cpanel.uapi_binary', '/usr/bin/uapi');

        $arguments = [$binary, '--output=json', 'Mysql', $function];

        foreach ($parameters as $key => $value) {
            $arguments[] = $key.'='.$value;
        }

        $process = new Process($arguments);
        $process->setTimeout((float) config('platform.provisioning.cpanel.timeout_seconds', 60));
        $process->run();

        if (! $process->isSuccessful()) {
            throw new RuntimeException(
                'cPanel database provisioning command failed: '.trim($process->getErrorOutput() ?: $process->getOutput())
            );
        }

        $payload = json_decode($process->getOutput(), true);

        if (! is_array($payload)) {
            throw new RuntimeException('cPanel returned an invalid database provisioning response.');
        }

        $result = $payload['result'] ?? null;

        if (! is_array($result) || (int) ($result['status'] ?? 0) !== 1) {
            $errors = $result['errors'] ?? ['Unknown cPanel database provisioning error.'];

            if (! is_array($errors)) {
                $errors = [$errors];
            }

            throw new RuntimeException(implode(' ', array_filter(array_map('strval', $errors))));
        }

        $data = $result['data'] ?? [];

        return is_array($data) ? $data : [];
    }
}
