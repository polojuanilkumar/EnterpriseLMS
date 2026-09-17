using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Common.Models
{
    public class ApiResponse<T>
    {
        public bool Success { get; init; }

        public string Message { get; init; } = string.Empty;

        public T? Data { get; init; }

        public object? Errors { get; init; }

        public static ApiResponse<T> Ok(
            T data,
            string message = "Request completed successfully.")
        {
            return new ApiResponse<T>
            {
                Success = true,
                Message = message,
                Data = data
            };
        }

        public static ApiResponse<T> Fail(
            string message,
            object? errors = null)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = message,
                Errors = errors
            };
        }
    }
}
