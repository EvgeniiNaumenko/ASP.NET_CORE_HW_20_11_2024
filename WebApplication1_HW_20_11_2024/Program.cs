

using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

app.Run(async (context) =>
{
    var response = context.Response;
    var request = context.Request;
    response.ContentType = "text/html; charset=utf-8";

    // Извлекаем токен из строки запроса
    string token = request.Query["token"];

    // Подключение к базе данных через Scope
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Если токен передан, пытаемся найти пользователя
    User? user = null;
    if (!string.IsNullOrEmpty(token))
    {
 
        user = db.Users.FirstOrDefault(u => u.Name == token);
    }

    // Для защищённых маршрутов проверяем наличие токена и пользователя
    bool isProtectedRoute = request.Path == "/courses" ||
                            request.Path == "/subscriptions" ||
                            request.Path == "/subscribe" ||
                            request.Path == "/unsubscribe";

    if (isProtectedRoute && (string.IsNullOrEmpty(token) || user == null))
    {
        response.Redirect("/?error=invalid_token"); // Перенаправляем на главную с сообщением об ошибке
        return;
    }

    // Обработка маршрутов
    if (request.Path == "/")
    {
        // Главная страница
        await response.WriteAsync(HtmlGenerator.GenerateLoginPage());
    }
    else if (request.Path == "/register" && request.Method == HttpMethods.Get)
    {
        // Страница регистрации
        await response.WriteAsync(HtmlGenerator.GenerateRegisterPage());
    }
    else if (request.Path == "/register" && request.Method == HttpMethods.Post)
    {
        // Обработка данных регистрации
        var form = await request.ReadFormAsync();
        string name = form["name"];
        string email = form["email"];
        string phone = form["phone"];
        string password = form["password"];

        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(password))
        {
            // Проверяем, нет ли уже пользователя с таким email
            if (db.Users.Any(u => u.Email == email))
            {
                response.Redirect("/");
            }
            else
            {
                // Добавляем нового пользователя
                var newUser = new User
                {
                    Name = name,
                    Email = email,
                    PhoneNumber = phone,
                    Password = password // Хранить пароль в незашифрованном виде не рекомендуется
                };

                db.Users.Add(newUser);
                await db.SaveChangesAsync();

                // Перенаправляем на главную страницу с токеном
                response.Redirect($"/");
            }
        }
        else
        {
            response.Redirect("/register?error=invalid_data");
        }
    }
    else if (request.Path == "/login" && request.Method == HttpMethods.Post)
    {
        // Считывание данных формы
        var form = await request.ReadFormAsync();
        string email = form["email"];
        string password = form["password"];

        // Проверяем email и пароль в базе данных
        user = db.Users.FirstOrDefault(u => u.Email == email && u.Password == password);

        if (user != null)
        {
            // Успешный вход
            response.Redirect($"/myPage?token={user.Id}"); // Перенаправление с токеном
        }
        else
        {
            // Ошибка входа
            response.Redirect("/?error=invalid_credentials");
        }
    }
    else if (request.Path == "/myPage")
    {
        // Список всех курсов
        var courses = db.Courses.ToList();
        await response.WriteAsync(HtmlGenerator.GenerateCoursesPage(courses, user.Name));
    }
    else if (request.Path == "/subscriptions")
    {
        // Получение всех подписок пользователя
        var subscriptions = db.Registrations
            .Where(r => r.UserId == user.Id)
            .Select(r => r.Course)
            .ToList();

        await response.WriteAsync(HtmlGenerator.GenerateSubscriptionsPage(subscriptions, user.Name));
    }
    else if (request.Path == "/addCourse")
    {
        if (user == null)
        {
            response.Redirect("/login"); // Или перенаправьте на другую страницу
            return;
        }
        if (request.Method == HttpMethods.Get)
        {
            // Возвращаем форму для заполнения
            await response.WriteAsync(HtmlGenerator.GenerateAddCourseForm(user.Name));
        }
        else if (request.Method == HttpMethods.Post)
        {
            // Считываем данные из POST-запроса
            var form = await request.ReadFormAsync();
            string title = form["title"];
            string description = form["description"];
            DateTime.TryParse(form["startDate"], out DateTime startDate);
            DateTime.TryParse(form["endDate"], out DateTime endDate);

            // Добавляем курс в базу данных
            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(description) && startDate != DateTime.MinValue && endDate != DateTime.MinValue)
            {
                db.Courses.Add(new Course
                {
                    Title = title,
                    Description = description,
                    StartDate = startDate,
                    EndDate = endDate
                });
                await db.SaveChangesAsync();
            }

            // Перенаправляем на страницу "/myPage"
            response.Redirect($"/myPage?token={user.Name}");
        }
    }
    else if (request.Path == "/unsubscribe" && user != null)
    {
        // Отписка от курса
        if (int.TryParse(request.Query["courseId"], out int courseId))
        {
            var subscription = db.Registrations.FirstOrDefault(r => r.UserId == user.Id && r.CourseId == courseId);
            if (subscription != null)
            {
                db.Registrations.Remove(subscription);
                await db.SaveChangesAsync();
            }
            response.Redirect($"/subscriptions?token={user.Name}"); // С возвращением токена
        }
        else
        {
            response.Redirect($"/subscriptions?token={user.Name}&error=invalid_course");
        }
    }
    else
    {
        // 404: Страница не найдена
        response.StatusCode = 404;
        await response.WriteAsync("<h1>404 - Страница не найдена</h1>");
    }
});

app.Run();


public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Course> Courses { get; set; } = null!;
    public DbSet<Registration> Registrations { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Registration>()
            .HasKey(r => new { r.UserId, r.CourseId });

        modelBuilder.Entity<Registration>()
            .HasOne(r => r.User)
            .WithMany(u => u.Registrations)
            .HasForeignKey(r => r.UserId);

        modelBuilder.Entity<Registration>()
            .HasOne(r => r.Course)
            .WithMany(c => c.Registrations)
            .HasForeignKey(r => r.CourseId);
    }
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public string Password { get; set; }

    public List<Registration> Registrations { get; set; }
}

public class Course
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public List<Registration> Registrations { get; set; }
}

public class Registration
{
    public int UserId { get; set; }
    public User User { get; set; }

    public int CourseId { get; set; }
    public Course Course { get; set; }
}

public static class HtmlGenerator
{
    public static string GenerateRegisterPage()
    {
        return @"
        <!DOCTYPE html>
        <html>
        <head>
            <title>Register</title>
            <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0-alpha3/dist/css/bootstrap.min.css' rel='stylesheet'>
        </head>
        <body class='container'>
            <h1 class='mt-5'>Register</h1>
            <form action='/register' method='post'>
                <div class='mb-3'>
                    <label for='name' class='form-label'>Name</label>
                    <input type='text' class='form-control' id='name' name='name' required>
                </div>
                <div class='mb-3'>
                    <label for='email' class='form-label'>Email</label>
                    <input type='email' class='form-control' id='email' name='email' required>
                </div>
                <div class='mb-3'>
                    <label for='phone' class='form-label'>Phone Number</label>
                    <input type='tel' class='form-control' id='phone' name='phone' required>
                </div>
                <div class='mb-3'>
                    <label for='password' class='form-label'>Password</label>
                    <input type='password' class='form-control' id='password' name='password' required>
                </div>
                <button type='submit' class='btn btn-primary'>Register</button>
            </form>
        </body>
        </html>";
    }
    public static string GenerateAddCourseForm(string userName)
    {
        return $@"
        <!DOCTYPE html>
        <html>
        <head>
            <title>Add Course</title>
            <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0-alpha3/dist/css/bootstrap.min.css' rel='stylesheet'>
        </head>
        <body class='container mt-5'>
            <h1>Add a New Course</h1>
            <form action='/addCourse?token={userName}' method='post'>
                <input type='hidden' name='token' value='{userName}' />
                <div class='mb-3'>
                    <label for='title' class='form-label'>Course Title</label>
                    <input type='text' id='title' name='title' class='form-control' required />
                </div>
                <div class='mb-3'>
                    <label for='description' class='form-label'>Course Description</label>
                    <textarea id='description' name='description' class='form-control' rows='3' required></textarea>
                </div>
                <div class='mb-3'>
                    <label for='startDate' class='form-label'>Start Date</label>
                    <input type='date' id='startDate' name='startDate' class='form-control' required />
                </div>
                <div class='mb-3'>
                    <label for='endDate' class='form-label'>End Date</label>
                    <input type='date' id='endDate' name='endDate' class='form-control' required />
                </div>
                <div class='d-flex justify-content-between'>
                    <button type='submit' class='btn btn-success'>Submit</button>
                    <a href='/myPage?token={userName}' class='btn btn-secondary'>Cancel</a>
                </div>
            </form>
        </body>
        </html>";
    }
    public static string GenerateHeader(string userName)
    {
        return $@"
        <nav class='navbar navbar-expand-lg navbar-light bg-light'>
            <a class='navbar-brand' href='/courses?token={userName}'>Мой сайт</a>
            <button class='navbar-toggler' type='button' data-bs-toggle='collapse' data-bs-target='#navbarNav' aria-controls='navbarNav' aria-expanded='false' aria-label='Переключить навигацию'>
                <span class='navbar-toggler-icon'></span>
            </button>
            <div class='collapse navbar-collapse' id='navbarNav'>
                <ul class='navbar-nav'>
                    <li class='nav-item'>
                        <a class='nav-link' href='/myPage?token={userName}'>Все курсы</a>
                    </li>
                    <li class='nav-item'>
                        <a class='nav-link' href='/myCourses?token={userName}'>Мои курсы</a>
                    </li>
                    <li class='nav-item'>
                        <a class='nav-link' href='/subscriptions?token={userName}'>Мои подписки</a>
                    </li>
                    <li class='nav-item'>
                        <a class='nav-link' href='/addCourse?token={userName}'>Добавить курс</a>
                    </li>
                    <li class='nav-item'>
                        <a class='nav-link' href='/'>Выход</a>
                    </li>
                </ul>
            </div>
        </nav>";
    }

    public static string GenerateLoginPage()
    {
        return @"
        <!DOCTYPE html>
        <html>
        <head>
            <title>Login</title>
            <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0-alpha3/dist/css/bootstrap.min.css' rel='stylesheet'>
        </head>
        <body class='container'>
            <h1 class='mt-5'>Login</h1>
            <form action='/login' method='post'>
                <div class='mb-3'>
                    <label for='email' class='form-label'>Email</label>
                    <input type='email' class='form-control' id='email' name='email' required>
                </div>
                <div class='mb-3'>
                    <label for='password' class='form-label'>Password</label>
                    <input type='password' class='form-control' id='password' name='password' required>
                </div>
                <button type='submit' class='btn btn-primary'>Login</button>
            </form>
            <a href='/register' class='btn btn-link mt-3'>Don't have an account? Register</a>
        </body>
        </html>";
    }

    public static string GenerateCoursesPage(List<Course> courses, string userName)
    {
        var rows = string.Join("", courses.Select(c => $@"
        <tr>
            <td>{c.Title}</td>
            <td>{c.Description}</td>
            <td>{c.StartDate:yyyy-MM-dd}</td>
            <td>{c.EndDate:yyyy-MM-dd}</td>
            <td>
                {GenerateSubscribeButton(c.Id, userName)}
            </td>
        </tr>
        "));

        return $@"
        <!DOCTYPE html>
        <html>
        <head>
            <title>Courses</title>
            <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0-alpha3/dist/css/bootstrap.min.css' rel='stylesheet'>
        </head>
        <body class='container'>
            {GenerateHeader(userName)}
            <h1 class='mt-5'>Courses</h1>
            <table class='table'>
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Description</th>
                        <th>Start Date</th>
                        <th>End Date</th>
                        <th>Action</th>
                    </tr>
                </thead>
                <tbody>
                    {rows}
                </tbody>
            </table>
        </body>
        </html>";
    }

    public static string GenerateSubscriptionsPage(List<Course> subscriptions, string userName)
    {
        var rows = string.Join("", subscriptions.Select(c => $@"
        <tr>
            <td>{c.Title}</td>
            <td>{c.Description}</td>
            <td>{c.StartDate:yyyy-MM-dd}</td>
            <td>{c.EndDate:yyyy-MM-dd}</td>
            <td>
                <form action='/unsubscribe' method='get'>
                    <input type='hidden' name='courseId' value='{c.Id}' />
                    <button type='submit' class='btn btn-danger'>Unsubscribe</button>
                </form>
            </td>
        </tr>
        "));

        return $@"
        <!DOCTYPE html>
        <html>
        <head>
            <title>Subscriptions</title>
            <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0-alpha3/dist/css/bootstrap.min.css' rel='stylesheet'>
        </head>
        <body class='container'>
            {GenerateHeader(userName)}
            <h1 class='mt-5'>My Subscriptions</h1>
            <table class='table'>
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Description</th>
                        <th>Start Date</th>
                        <th>End Date</th>
                        <th>Action</th>
                    </tr>
                </thead>
                <tbody>
                    {rows}
                </tbody>
            </table>
        </body>
        </html>";
    }

    private static string GenerateSubscribeButton(int courseId, string userName)
    {
        return $@"
        <form action='/subscribe' method='get'>
            <input type='hidden' name='courseId' value='{courseId}' />
            <input type='hidden' name='token' value='{userName}' />
            <button type='submit' class='btn btn-primary'>Subscribe</button>
        </form>";
    }
}
