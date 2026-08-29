using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Application.Common;
using POS.Application.Common.Models;
using POS.Application.Employees.DTOs;
using POS.Application.Employees.Services;
using POS.Domain.Enums;

namespace POS.Api.Controllers;

[Authorize(Policy = Permissions.ManageEmployees)]
public class EmployeesController : ApiControllerBase
{
    private readonly IEmployeeService _employeeService;

    public EmployeesController(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> CreateEmployee([FromBody] CreateEmployeeDto request, CancellationToken cancellationToken)
    {
        var result = await _employeeService.CreateEmployeeAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetEmployeeById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _employeeService.GetEmployeeByIdAsync(id, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("store/{storeId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EmployeeDto>>), 200)]
    public async Task<IActionResult> GetEmployeesByStore(Guid storeId, CancellationToken cancellationToken)
    {
        var result = await _employeeService.GetEmployeesByStoreAsync(storeId, cancellationToken);
        return HandleResult(result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), 200)]
    public async Task<IActionResult> UpdateEmployee(Guid id, [FromBody] UpdateEmployeeDto request, CancellationToken cancellationToken)
    {
        var result = await _employeeService.UpdateEmployeeAsync(id, request, cancellationToken);
        return HandleResult(result);
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), 200)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] EmployeeStatus newStatus, CancellationToken cancellationToken)
    {
        var result = await _employeeService.ChangeStatusAsync(id, newStatus, cancellationToken);
        return HandleResult(result);
    }
}
