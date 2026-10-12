using Xunit;

namespace BusinessOS.Restaurant.Tests;

/// <summary>
/// Batch 4 regression guards: the Users &amp; Roles window keeps its
/// Users/Roles/Permissions tabs, the permission matrix features and the
/// tenant role API integration, while the module stays reachable from the
/// existing top navigation workspace.
/// </summary>
public sealed class UsersRolesWorkspaceTests
{
    [Fact]
    public void Users_and_roles_window_keeps_three_management_tabs()
    {
        var source = Window();
        Assert.Contains("Tab(\"Users\"", source);
        Assert.Contains("Tab(\"Roles\"", source);
        Assert.Contains("Tab(\"Permissions\"", source);
        Assert.Contains("RestaurantSettingsTabItemStyle", source);
    }

    [Fact]
    public void Users_and_roles_window_uses_tenant_authorized_endpoints()
    {
        var source = Window();
        Assert.Contains("Endpoint(\"users\")", source);
        Assert.Contains("Endpoint(\"users/\"", source);
        Assert.Contains("Endpoint(\"roles\")", source);
        Assert.Contains("Endpoint(\"roles/\"", source);
        Assert.Contains("/api/v1/desktop/", source);
        Assert.Contains("AuthenticationHeaderValue(\"Bearer\"", source);
    }

    [Fact]
    public void Permission_matrix_supports_grouping_select_all_clear_all_counts_and_unsaved_drafts()
    {
        var source = Window();
        Assert.Contains("BuildPermissionMatrix", source);
        Assert.Contains("Select all", source);
        Assert.Contains("Clear all", source);
        Assert.Contains("SetGroupPermissions(group, true)", source);
        Assert.Contains("SetGroupPermissions(group, false)", source);
        Assert.Contains("_draftPermissions", source);
        Assert.Contains("HasUnsavedPermissionChanges", source);
        Assert.Contains("Unsaved changes", source);
        Assert.Contains("permissions selected", source);
        Assert.Contains("🔒", source);
    }

    [Fact]
    public void Role_management_covers_crud_counts_search_and_protected_system_roles()
    {
        var source = Window();
        Assert.Contains("CreateRoleAsync", source);
        Assert.Contains("SaveRoleAsync", source);
        Assert.Contains("DeleteRoleAsync", source);
        Assert.Contains("nameof(RoleDto.UserCount)", source);
        Assert.Contains("nameof(RoleDto.PermissionCount)", source);
        Assert.Contains("ApplyRoleFilter", source);
        Assert.Contains("Search roles", source);
        Assert.Contains("IsProtected", source);
        Assert.Contains("System roles cannot be deleted", source);
    }

    [Fact]
    public void User_forms_assign_tenant_roles_and_prevent_duplicate_submissions()
    {
        var source = Window();
        Assert.Contains("nameof(RoleOption.Label)", source);
        Assert.Contains("[\"role\"] = role.Key", source);
        Assert.Contains("nameof(DesktopStaffAccount.RoleDisplay)", source);
        Assert.Contains("_saveUser.IsEnabled = false;", source);
        Assert.Contains("_savePermissions.IsEnabled = false;", source);
        Assert.Contains("DeleteRoleAsync", source);
    }

    [Fact]
    public void Module_remains_reachable_from_the_existing_workspace_navigation()
    {
        var pages = Source("BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs");
        Assert.Contains("new DesktopUserManagementWindow(session)", pages);
        Assert.Contains("\"users\" => await UsersAsync()", pages);

        var home = Source("BusinessOS.Restaurant.Desktop", "ModuleHome.cs");
        Assert.Contains("\"users\"", home);
        Assert.Contains("Users & Roles", home);
    }

    private static string Window() => Source("BusinessOS.Restaurant.Desktop", "DesktopUserManagementWindow.cs");

    private static string Source(params string[] relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;

        return File.ReadAllText(Path.Combine(
            dir?.FullName ?? throw new DirectoryNotFoundException(),
            "desktop", "src", Path.Combine(relativePath)));
    }
}
