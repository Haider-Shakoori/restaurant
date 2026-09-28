<?php

namespace App\Enums;

enum OnboardingStep: string
{
    case Restaurant = 'restaurant';
    case Branch = 'branch';
    case Floors = 'floors';
    case Tables = 'tables';
    case Kitchen = 'kitchen';
    case Categories = 'categories';
    case Items = 'items';
    case Employees = 'employees';
    case Assignments = 'assignments';
    case Printer = 'printer';
    case AndroidDevices = 'android_devices';
    case OpeningInventory = 'opening_inventory';
    case Finish = 'finish';

    public function label(): string
    {
        return match ($this) {
            self::Restaurant => 'Restaurant',
            self::Branch => 'Branch',
            self::Floors => 'Floors / Sections',
            self::Tables => 'Tables',
            self::Kitchen => 'Kitchen Stations',
            self::Categories => 'Menu Categories',
            self::Items => 'Menu Items',
            self::Employees => 'Employees',
            self::Assignments => 'Waiter Assignments',
            self::Printer => 'Printer',
            self::AndroidDevices => 'Android Devices',
            self::OpeningInventory => 'Opening Inventory',
            self::Finish => 'Finish',
        };
    }

    public function isImplemented(): bool
    {
        return in_array($this, [self::Restaurant, self::Branch], true);
    }

    public function next(): ?self
    {
        $cases = self::cases();
        $index = array_search($this, $cases, true);

        return $index === false || ! isset($cases[$index + 1]) ? null : $cases[$index + 1];
    }
}
