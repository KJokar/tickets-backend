using System.ComponentModel.DataAnnotations;

namespace Database.Enums.Identity;

public enum UserRole
{
    [Display(Name = "کاربر عادی")]
    Client = 1,
    [Display(Name = "پشتیبان")]
    Agent = 2
}
