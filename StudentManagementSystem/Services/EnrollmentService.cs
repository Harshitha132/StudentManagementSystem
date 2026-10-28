
using Microsoft.Data.SqlClient;
using System.Data;
using StudentCourseManagement.Helpers;

namespace StudentCourseManagement.Services;

public class EnrollmentService
{
    public async Task<int> EnrollStudentAsync(
        int studentId,
        int courseId)
    {
        await using SqlConnection connection =
            DatabaseHelper.CreateConnection();

        await connection.OpenAsync();

        await using SqlTransaction transaction =
            (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            // 1. Check whether the student and course exist
            const string checkSql = """
                SELECT
                    CASE WHEN EXISTS
                    (
                        SELECT 1
                        FROM dbo.Students
                        WHERE StudentId = @StudentId
                    )
                    AND EXISTS
                    (
                        SELECT 1
                        FROM dbo.Courses
                        WHERE CourseId = @CourseId
                    )
                    THEN 1 ELSE 0 END;
                """;

            await using (SqlCommand command =
                new SqlCommand(checkSql, connection, transaction))
            {
                command.Parameters.Add(
                    "@StudentId", SqlDbType.Int).Value = studentId;

                command.Parameters.Add(
                    "@CourseId", SqlDbType.Int).Value = courseId;

                int exists = Convert.ToInt32(
                    await command.ExecuteScalarAsync());

                if (exists == 0)
                {
                    throw new InvalidOperationException(
                        "Student or course does not exist.");
                }
            }

            // 2. Insert enrollment
            const string enrollmentSql = """
                INSERT INTO dbo.Enrollments
                    (StudentId, CourseId, Status)
                VALUES
                    (@StudentId, @CourseId, N'Enrolled');

                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;

            int enrollmentId;

            await using (SqlCommand command =
                new SqlCommand(enrollmentSql, connection, transaction))
            {
                command.Parameters.Add(
                    "@StudentId", SqlDbType.Int).Value = studentId;

                command.Parameters.Add(
                    "@CourseId", SqlDbType.Int).Value = courseId;

                enrollmentId = Convert.ToInt32(
                    await command.ExecuteScalarAsync());
            }

            // 3. Insert audit log
            const string auditSql = """
                INSERT INTO dbo.AuditLogs
                    (EntityName, EntityId, Action, Details)
                VALUES
                    (@EntityName, @EntityId, @Action, @Details);
                """;

            await using (SqlCommand command =
                new SqlCommand(auditSql, connection, transaction))
            {
                command.Parameters.Add(
                    "@EntityName", SqlDbType.NVarChar, 50).Value =
                    "Enrollment";

                command.Parameters.Add(
                    "@EntityId", SqlDbType.Int).Value = enrollmentId;

                command.Parameters.Add(
                    "@Action", SqlDbType.NVarChar, 20).Value = "INSERT";

                command.Parameters.Add(
                    "@Details", SqlDbType.NVarChar, 500).Value =
                    $"Student {studentId} enrolled in course {courseId}.";

                await command.ExecuteNonQueryAsync();
            }

            // 4. Commit both inserts together
            await transaction.CommitAsync();

            return enrollmentId;
        }
        catch
        {
            // Roll back both inserts if anything fails
            await transaction.RollbackAsync();
            throw;
        }
    }
}
